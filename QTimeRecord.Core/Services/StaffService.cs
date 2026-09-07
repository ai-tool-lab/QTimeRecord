using Microsoft.Extensions.Logging;
using QTimeRecord.Core.Data.Repositories;
using QTimeRecord.Core.Domain;

namespace QTimeRecord.Core.Services;

/// <summary>スタッフの登録・編集の入力。</summary>
public sealed record StaffDraft
{
    /// <summary>編集する場合の対象。新規登録なら null。</summary>
    public Guid? StaffId { get; init; }

    public required string Name { get; init; }

    public string? NameKana { get; init; }

    /// <summary>社員番号。CSV で給与システムと突き合わせるキー。店舗内で重複させない。</summary>
    public string? StaffNo { get; init; }

    /// <summary>区分（社員／アルバイト／パート）。集計はせず、表示と絞り込みのみに使う。</summary>
    public string? EmploymentType { get; init; }

    public StaffStatus Status { get; init; } = StaffStatus.Active;
}

/// <summary>スタッフ1名と、その QR の発行状況。</summary>
/// <param name="Staff">スタッフ。</param>
/// <param name="IssuedAt">有効な QR の発行日時。未発行・失効済みなら null。</param>
public sealed record StaffListItem(Staff Staff, DateTime? IssuedAt)
{
    public bool HasActiveQr => IssuedAt is not null;
}

/// <summary>名簿の絞り込み。</summary>
public enum StaffFilter
{
    /// <summary>全員（退職者を含む）。</summary>
    All,

    /// <summary>区分が「社員」。</summary>
    FullTime,

    /// <summary>区分が「アルバイト」または「パート」。</summary>
    PartTime,

    /// <summary>退職済。</summary>
    Retired,
}

public interface IStaffService
{
    /// <summary>名簿を取り出す。検索は氏名・フリガナ・社員番号の部分一致。</summary>
    Task<IReadOnlyList<StaffListItem>> ListAsync(
        StaffFilter filter = StaffFilter.All, string? search = null, CancellationToken ct = default);

    /// <summary>保存前の検証。画面はこれを見てエラーを出す。</summary>
    Task<string?> ValidateAsync(StaffDraft draft, CancellationToken ct = default);

    /// <summary>登録する。<b>QR も同時に発行する</b>（→ plan.md 12-3）。</summary>
    Task<Staff> AddAsync(StaffDraft draft, CancellationToken ct = default);

    /// <summary>編集する。在籍状態はここでは変えない。</summary>
    Task<Staff> UpdateAsync(StaffDraft draft, CancellationToken ct = default);

    /// <summary>在籍状態を変える。退職済にすると有効な QR を失効させる（→ plan.md Q15）。</summary>
    Task<Staff> ChangeStatusAsync(Guid staffId, StaffStatus status, CancellationToken ct = default);

    /// <summary>QR を再発行する。旧トークンは失効する。</summary>
    Task<StaffQrToken> ReissueQrAsync(Guid staffId, CancellationToken ct = default);
}

/// <summary>
/// スタッフの名簿管理。
///
/// <b>物理削除は行わない。</b>打刻レコードが参照しており、消すと過去の勤務記録が壊れる
/// （→ CLAUDE.md 必須ルール7）。退職は <see cref="StaffStatus.Retired"/> で表す。
/// </summary>
public sealed class StaffService(
    IStoreRepository stores,
    IStaffRepository staff,
    IQrTokenRepository tokens,
    IQrTokenService qrTokens,
    ILogger<StaffService> logger) : IStaffService
{
    public async Task<IReadOnlyList<StaffListItem>> ListAsync(
        StaffFilter filter = StaffFilter.All,
        string? search = null,
        CancellationToken ct = default)
    {
        var store = await stores.GetAsync(ct);

        if (store is null)
        {
            return [];
        }

        // 退職者の絞り込みだけは DB 側で在籍状態を指定できる。
        // 区分は自由入力の文字列なので、取り出してから絞る。
        var status = filter == StaffFilter.Retired ? StaffStatus.Retired : (StaffStatus?)null;
        var found = await staff.ListAsync(store.Id, status, search, ct);

        var items = new List<StaffListItem>();

        foreach (var member in found.Where(m => Matches(m, filter)))
        {
            var token = await tokens.GetActiveForStaffAsync(member.Id, ct);

            items.Add(new StaffListItem(member, token?.IssuedAt));
        }

        return items;
    }

    public async Task<string?> ValidateAsync(StaffDraft draft, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        if (string.IsNullOrWhiteSpace(draft.Name))
        {
            return "氏名を入力してください。";
        }

        if (string.IsNullOrWhiteSpace(draft.StaffNo))
        {
            return null;
        }

        var store = await stores.GetAsync(ct);

        if (store is null)
        {
            return "店舗が登録されていません。";
        }

        // 重複すると CSV を給与システムへ渡したときに別人の勤怠が混ざる。
        return await staff.StaffNoExistsAsync(store.Id, draft.StaffNo.Trim(), draft.StaffId, ct)
            ? "この社員番号はすでに使われています。"
            : null;
    }

    public async Task<Staff> AddAsync(StaffDraft draft, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        if (await ValidateAsync(draft, ct) is { } error)
        {
            throw new ArgumentException(error, nameof(draft));
        }

        var store = await stores.GetAsync(ct)
            ?? throw new InvalidOperationException("店舗が登録されていません。");

        var member = new Staff
        {
            Id = Guid.CreateVersion7(),
            StoreId = store.Id,
            Name = draft.Name.Trim(),
            NameKana = Normalize(draft.NameKana),
            StaffNo = Normalize(draft.StaffNo),
            EmploymentType = Normalize(draft.EmploymentType),
            Status = draft.Status,
        };

        await staff.AddAsync(member, ct);

        // 登録と同時に発行する。あとで発行する運用にすると、
        // カードを渡し忘れたスタッフが打刻できないまま出勤する。
        await qrTokens.IssueAsync(store.Id, member.Id, ct);

        logger.LogInformation("スタッフを登録しました: {StaffId}", member.Id);

        return member;
    }

    public async Task<Staff> UpdateAsync(StaffDraft draft, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var staffId = draft.StaffId
            ?? throw new ArgumentException("編集するスタッフが指定されていません。", nameof(draft));

        if (await ValidateAsync(draft, ct) is { } error)
        {
            throw new ArgumentException(error, nameof(draft));
        }

        var member = await staff.GetByIdAsync(staffId, ct)
            ?? throw new InvalidOperationException("スタッフが見つかりません。");

        member.Name = draft.Name.Trim();
        member.NameKana = Normalize(draft.NameKana);
        member.StaffNo = Normalize(draft.StaffNo);
        member.EmploymentType = Normalize(draft.EmploymentType);

        await staff.UpdateAsync(member, ct);

        logger.LogInformation("スタッフを更新しました: {StaffId}", member.Id);

        return member;
    }

    public async Task<Staff> ChangeStatusAsync(
        Guid staffId, StaffStatus status, CancellationToken ct = default)
    {
        var member = await staff.GetByIdAsync(staffId, ct)
            ?? throw new InvalidOperationException("スタッフが見つかりません。");

        member.Status = status;

        await staff.UpdateAsync(member, ct);

        if (status == StaffStatus.Retired)
        {
            // 手で失効させる運用にすると、忘れたときに退職者のQRで打刻できてしまう（→ plan.md Q15）。
            var revoked = await qrTokens.RevokeAsync(staffId, ct);

            logger.LogInformation(
                "退職に伴い QR を失効させました: {StaffId} {Count}件", staffId, revoked);
        }

        logger.LogInformation("在籍状態を変更しました: {StaffId} {Status}", staffId, status);

        return member;
    }

    public async Task<StaffQrToken> ReissueQrAsync(Guid staffId, CancellationToken ct = default)
    {
        var member = await staff.GetByIdAsync(staffId, ct)
            ?? throw new InvalidOperationException("スタッフが見つかりません。");

        // 発行は「旧を失効 → 新を発行」を1つのトランザクションで行う（→ QrTokenRepository）。
        var token = await qrTokens.IssueAsync(member.StoreId, staffId, ct);

        logger.LogInformation("QR を再発行しました: {StaffId}", staffId);

        return token;
    }

    /// <summary>区分での絞り込み。区分は自由入力なので、含まれる語で判断する。</summary>
    private static bool Matches(Staff member, StaffFilter filter) => filter switch
    {
        StaffFilter.FullTime => Contains(member.EmploymentType, "社員"),
        StaffFilter.PartTime =>
            Contains(member.EmploymentType, "アルバイト") || Contains(member.EmploymentType, "パート"),
        StaffFilter.Retired => member.Status == StaffStatus.Retired,
        _ => true,
    };

    private static bool Contains(string? value, string keyword)
        => value is not null && value.Contains(keyword, StringComparison.Ordinal);

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
