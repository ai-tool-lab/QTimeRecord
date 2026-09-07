using Microsoft.Extensions.Logging;
using QTimeRecord.Core.Data.Repositories;
using QTimeRecord.Core.Domain;
using QTimeRecord.Core.Infrastructure;

namespace QTimeRecord.Core.Services;

/// <summary>管理者が手で入力する打刻1件。新規と修正の両方に使う。</summary>
public sealed record TimeRecordDraft
{
    /// <summary>修正する打刻の ID。新規登録なら null。</summary>
    public Guid? RecordId { get; init; }

    public required Guid StaffId { get; init; }

    /// <summary>
    /// 営業日。
    ///
    /// <b>打刻日時から自動で決めない。</b>自動判定（→ plan.md Q2-a）が取りこぼした分を
    /// 管理者が直せる唯一の手段であり、ここで上書きできることが要件そのもの。
    /// </summary>
    public required DateOnly WorkDate { get; init; }

    public required TimeRecordType RecordType { get; init; }

    public required DateTime RecordedAt { get; init; }

    /// <summary>修正理由のメモ（→ plan.md Q20）。</summary>
    public string? Note { get; init; }
}

/// <summary>入力の検証結果。</summary>
/// <param name="Error">保存できない理由。問題なければ null。</param>
/// <param name="Warning">保存はできるが確認したいこと。無ければ null。</param>
public sealed record EditValidation(string? Error, string? Warning)
{
    public static readonly EditValidation Ok = new(null, null);

    public bool IsValid => Error is null;

    public bool HasWarning => Warning is not null;
}

public interface ITimeRecordEditService
{
    /// <summary>保存前の検証。画面はこれを見てエラー表示・確認ダイアログを出す。</summary>
    Task<EditValidation> ValidateAsync(TimeRecordDraft draft, CancellationToken ct = default);

    /// <summary>手動で打刻を追加する。</summary>
    Task<TimeRecord> AddAsync(TimeRecordDraft draft, CancellationToken ct = default);

    /// <summary>既存の打刻を書き換える。<b>修正履歴は残さない</b>（仕様どおり）。</summary>
    Task<TimeRecord> UpdateAsync(TimeRecordDraft draft, CancellationToken ct = default);

    /// <summary>誤登録を取り消す。呼ぶ前に必ず確認を取ること。</summary>
    Task DeleteAsync(Guid recordId, CancellationToken ct = default);
}

/// <summary>
/// 打刻の手動登録・修正・削除。
///
/// QR 打刻と分けているのは、<b>登録方法を必ず残す</b>ため。
/// 手が入った打刻を後から見分けられることは要件で、一覧の色分けの根拠になる。
/// </summary>
public sealed class TimeRecordEditService(
    IStoreRepository stores,
    IStaffRepository staff,
    ITimeRecordRepository records,
    IClock clock,
    ILogger<TimeRecordEditService> logger) : ITimeRecordEditService
{
    public async Task<EditValidation> ValidateAsync(
        TimeRecordDraft draft, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        if (await staff.GetByIdAsync(draft.StaffId, ct) is null)
        {
            return new EditValidation("スタッフが見つかりません。", null);
        }

        if (draft.RecordId is { } id && await records.GetByIdAsync(id, ct) is null)
        {
            return new EditValidation("対象の打刻が見つかりません。一覧を読み込み直してください。", null);
        }

        // 未来の打刻は打ち間違いのことが多いが、シフトの先行入力もありうる。
        // 弾かずに確認だけ取る。
        if (draft.RecordedAt > clock.Now)
        {
            return new EditValidation(null, "未来の日時です。このまま登録しますか？");
        }

        return EditValidation.Ok;
    }

    public async Task<TimeRecord> AddAsync(TimeRecordDraft draft, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var store = await stores.GetAsync(ct)
            ?? throw new InvalidOperationException("店舗が登録されていません。");

        var record = new TimeRecord
        {
            Id = Guid.CreateVersion7(),
            StoreId = store.Id,
            StaffId = draft.StaffId,
            RecordType = draft.RecordType,
            RecordedAt = draft.RecordedAt,
            WorkDate = draft.WorkDate,
            EntryMethod = EntryMethod.ManualAdd,
            Note = Normalize(draft.Note),
            IsOutsideBusinessHours = !BusinessDayResolver.IsWithinBusinessHours(
                draft.RecordedAt, BusinessDaySettings.From(store)),
        };

        await records.AddAsync(record, ct);

        logger.LogInformation(
            "打刻を手動登録しました: {StaffId} {RecordType} {WorkDate}",
            draft.StaffId, draft.RecordType, draft.WorkDate);

        return record;
    }

    public async Task<TimeRecord> UpdateAsync(TimeRecordDraft draft, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var recordId = draft.RecordId
            ?? throw new ArgumentException("修正する打刻が指定されていません。", nameof(draft));

        var store = await stores.GetAsync(ct)
            ?? throw new InvalidOperationException("店舗が登録されていません。");

        var record = await records.GetByIdAsync(recordId, ct)
            ?? throw new InvalidOperationException("対象の打刻が見つかりません。");

        record.RecordType = draft.RecordType;
        record.RecordedAt = draft.RecordedAt;
        record.WorkDate = draft.WorkDate;
        record.Note = Normalize(draft.Note);
        record.IsOutsideBusinessHours = !BusinessDayResolver.IsWithinBusinessHours(
            draft.RecordedAt, BusinessDaySettings.From(store));

        // 手で入れ直したものは、元が QR でも手修正として残す。
        // 上書きすると、その打刻が本人のものか管理者のものか分からなくなる。
        record.EntryMethod = EntryMethod.ManualEdit;

        await records.UpdateAsync(record, ct);

        logger.LogInformation(
            "打刻を修正しました: {RecordId} {RecordType} {WorkDate}",
            recordId, draft.RecordType, draft.WorkDate);

        return record;
    }

    public async Task DeleteAsync(Guid recordId, CancellationToken ct = default)
    {
        await records.DeleteAsync(recordId, ct);

        logger.LogWarning("打刻を削除しました: {RecordId}", recordId);
    }

    private static string? Normalize(string? note)
        => string.IsNullOrWhiteSpace(note) ? null : note.Trim();
}
