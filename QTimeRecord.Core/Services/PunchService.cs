using Microsoft.Extensions.Logging;
using QTimeRecord.Core.Data.Repositories;
using QTimeRecord.Core.Domain;
using QTimeRecord.Core.Infrastructure;

namespace QTimeRecord.Core.Services;

/// <summary>打刻の結果。</summary>
public enum PunchStatus
{
    /// <summary>記録した。</summary>
    Recorded,

    /// <summary>異常な遷移のため、確認を取ってから記録する。まだ記録していない。</summary>
    NeedsConfirmation,

    /// <summary>連打・二度読み。記録していない。</summary>
    Duplicate,

    /// <summary>打刻できないスタッフ（休職・退職・未登録）。記録していない。</summary>
    Rejected,

    /// <summary>保存に失敗した。記録していない。</summary>
    Failed,
}

/// <summary>打刻の要求。</summary>
public sealed record PunchRequest
{
    public required Guid StaffId { get; init; }

    public required TimeRecordType RecordType { get; init; }

    /// <summary>
    /// 異常な遷移であることを確認済みか。
    ///
    /// false のまま異常な遷移を要求すると、記録せずに
    /// <see cref="PunchStatus.NeedsConfirmation"/> を返す。
    /// 画面で確認を取ってから true で呼び直す。
    /// </summary>
    public bool WarningConfirmed { get; init; }

    /// <summary>登録方法。QR 打刻以外（管理者の手動登録）から使う余地を残す。</summary>
    public EntryMethod EntryMethod { get; init; } = EntryMethod.Qr;
}

/// <summary>打刻の結果。</summary>
public sealed record PunchOutcome
{
    public required PunchStatus Status { get; init; }

    /// <summary>画面に出す文言。</summary>
    public required string Message { get; init; }

    /// <summary>記録した打刻。記録していない場合は null。</summary>
    public TimeRecord? Record { get; init; }

    /// <summary>打刻を判定した時点の状態。確認ダイアログの文面に使う。</summary>
    public PunchState State { get; init; }

    public bool IsRecorded => Status == PunchStatus.Recorded;
}

/// <summary>打刻画面に出す、そのスタッフの現況。</summary>
/// <param name="Staff">対象スタッフ。</param>
/// <param name="State">現在の勤務状態。</param>
/// <param name="WorkDate">いま打刻するとどの営業日に付くか。</param>
/// <param name="LatestRecord">直近の打刻。誤打刻に気づけるよう画面に出す。</param>
public sealed record PunchContext(
    Staff Staff, PunchState State, DateOnly WorkDate, TimeRecord? LatestRecord);

public interface IPunchService
{
    /// <summary>打刻画面に出す現況を取り出す。</summary>
    Task<PunchContext?> GetContextAsync(Guid staffId, CancellationToken ct = default);

    /// <summary>打刻する。</summary>
    Task<PunchOutcome> PunchAsync(PunchRequest request, CancellationToken ct = default);
}

/// <summary>
/// 打刻の中核。営業日の決定・状態判定・保存をここで完結させる。
///
/// <b>保存に失敗したものを成功として返さない</b>（→ CLAUDE.md 必須ルール3）。
/// 打刻は勤怠の証跡であり、静かに失われてはいけない。
/// </summary>
public sealed class PunchService(
    IStoreRepository stores,
    IStaffRepository staff,
    ITimeRecordRepository records,
    IClock clock,
    ILogger<PunchService> logger) : IPunchService
{
    public async Task<PunchContext?> GetContextAsync(Guid staffId, CancellationToken ct = default)
    {
        var target = await staff.GetByIdAsync(staffId, ct);
        var store = await stores.GetAsync(ct);

        if (target is null || store is null)
        {
            logger.LogWarning("打刻の対象が見つかりません: {StaffId}", staffId);
            return null;
        }

        var workDate = await ResolveWorkDateAsync(staffId, store, clock.Now, ct);
        var today = await records.ListByWorkDateAsync(store.Id, staffId, workDate, ct);

        return new PunchContext(
            target,
            PunchStateMachine.Resolve(today),
            workDate,
            await records.GetLatestAsync(staffId, ct));
    }

    public async Task<PunchOutcome> PunchAsync(PunchRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var store = await stores.GetAsync(ct);
        var target = await staff.GetByIdAsync(request.StaffId, ct);

        if (store is null || target is null)
        {
            logger.LogWarning("打刻の対象が見つかりません: {StaffId}", request.StaffId);
            return Reject("打刻できませんでした。管理者へ連絡してください。");
        }

        // 打刻画面を開いたまま時間が経つこともある。書き込む直前にもう一度見る。
        if (target.Status != StaffStatus.Active)
        {
            logger.LogWarning(
                "在籍状態が {Status} のため打刻を拒否しました: {StaffId}", target.Status, target.Id);

            return Reject("打刻できません。管理者へ連絡してください。");
        }

        var now = clock.Now;
        var settings = BusinessDaySettings.From(store);
        var workDate = await ResolveWorkDateAsync(request.StaffId, store, now, ct);

        var today = await records.ListByWorkDateAsync(store.Id, request.StaffId, workDate, ct);
        var state = PunchStateMachine.Resolve(today);

        var judgement = PunchStateMachine.Evaluate(
            state, request.RecordType, await records.GetLatestAsync(request.StaffId, ct), now);

        if (judgement.Decision == PunchDecision.Duplicate)
        {
            logger.LogInformation(
                "重複打刻のため記録しませんでした: {StaffId} {RecordType}",
                target.Id, request.RecordType);

            return new PunchOutcome
            {
                Status = PunchStatus.Duplicate,
                Message = judgement.Reason ?? "すでに打刻済みです。",
                State = state,
            };
        }

        if (judgement.NeedsConfirmation && !request.WarningConfirmed)
        {
            return new PunchOutcome
            {
                Status = PunchStatus.NeedsConfirmation,
                Message = judgement.Reason ?? string.Empty,
                State = state,
            };
        }

        var record = new TimeRecord
        {
            Id = Guid.CreateVersion7(),
            StoreId = store.Id,
            StaffId = target.Id,
            RecordType = request.RecordType,
            RecordedAt = now,
            WorkDate = workDate,
            EntryMethod = request.EntryMethod,
            IsOutsideBusinessHours = !BusinessDayResolver.IsWithinBusinessHours(now, settings),
        };

        try
        {
            await records.AddAsync(record, ct);
        }
        catch (Exception ex)
        {
            // ここを握り潰すと、打刻できたつもりの勤怠が丸ごと消える。
            logger.LogError(
                ex, "打刻を保存できませんでした: {StaffId} {RecordType}", target.Id, request.RecordType);

            return new PunchOutcome
            {
                Status = PunchStatus.Failed,
                Message = "打刻を保存できませんでした。もう一度お試しください。",
                State = state,
            };
        }

        logger.LogInformation(
            "打刻を記録しました: {StaffId} {RecordType} {WorkDate} 異常={Warned} 時間外={Outside}",
            target.Id,
            request.RecordType,
            workDate,
            judgement.NeedsConfirmation,
            record.IsOutsideBusinessHours);

        return new PunchOutcome
        {
            Status = PunchStatus.Recorded,
            Message = $"{Describe(request.RecordType)}を記録しました",
            Record = record,
            State = PunchStateMachine.StateAfter(request.RecordType),
        };
    }

    /// <summary>打刻種別の表示名。</summary>
    public static string Describe(TimeRecordType type) => type switch
    {
        TimeRecordType.ClockIn => "出勤",
        TimeRecordType.ClockOut => "退勤",
        TimeRecordType.BreakStart => "中抜け開始",
        TimeRecordType.BreakEnd => "中抜け終了",
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    /// <summary>
    /// 営業日を決める。
    ///
    /// 閉店中の打刻は、未退勤の出勤が残っているかどうかで前営業日か当日かが変わる
    /// （→ plan.md Q2-a）。判断材料はここでだけ集め、判定自体は
    /// <see cref="BusinessDayResolver"/> に任せる。
    /// </summary>
    private async Task<DateOnly> ResolveWorkDateAsync(
        Guid staffId, Store store, DateTime now, CancellationToken ct)
    {
        var openClockIn = await records.GetOpenClockInAsync(staffId, ct);

        return BusinessDayResolver.Resolve(
            now, BusinessDaySettings.From(store), openClockIn?.RecordedAt);
    }

    private static PunchOutcome Reject(string message)
        => new() { Status = PunchStatus.Rejected, Message = message };
}
