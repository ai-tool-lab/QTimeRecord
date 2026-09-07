using QTimeRecord.Core.Data.Repositories;
using QTimeRecord.Core.Domain;

namespace QTimeRecord.Core.Services;

/// <summary>一覧の絞り込み。</summary>
public enum AttendanceFilter
{
    /// <summary>すべて。</summary>
    All,

    /// <summary>要確認（打刻の欠け・異常な遷移）。</summary>
    NeedsReview,

    /// <summary>打刻漏れ（出勤か退勤が無い）。</summary>
    Incomplete,
}

/// <summary>一覧の並び順。</summary>
public enum AttendanceSort
{
    /// <summary>営業日の降順（新しい日が上）。既定。</summary>
    DateDescending,

    /// <summary>営業日の昇順（古い日が上）。</summary>
    DateAscending,
}

/// <summary>一覧の1行。集計に氏名と区分を添えたもの。</summary>
/// <param name="Summary">日次の集計。</param>
/// <param name="StaffName">スタッフ氏名。</param>
/// <param name="StaffNo">社員番号。未設定なら null。</param>
/// <param name="EmploymentType">区分（社員／アルバイト／パート）。未設定なら null。</param>
public sealed record AttendanceRow(
    DailyAttendance Summary, string StaffName, string? StaffNo, string? EmploymentType)
{
    public DateOnly WorkDate => Summary.WorkDate;

    public AttendanceStatus Status => Summary.Status;
}

/// <summary>一覧の取得条件。</summary>
public sealed record AttendanceQuery
{
    public required int Year { get; init; }

    public required int Month { get; init; }

    /// <summary>絞り込むスタッフ。null なら全員。</summary>
    public Guid? StaffId { get; init; }

    public AttendanceFilter Filter { get; init; } = AttendanceFilter.All;

    /// <summary>
    /// 並び順。既定は新しい日が上。
    ///
    /// 管理者が最初に見たいのは直近の勤怠なので、開いた直後は新しい順にする。
    /// 月ぶんを通して確認するときは昇順へ切り替える。
    /// </summary>
    public AttendanceSort Sort { get; init; } = AttendanceSort.DateDescending;
}

public interface IAttendanceQueryService
{
    /// <summary>指定した営業月の一覧を取り出す。</summary>
    Task<IReadOnlyList<AttendanceRow>> GetAsync(
        AttendanceQuery query, CancellationToken ct = default);
}

/// <summary>
/// 勤務状況一覧の取得。
///
/// <b>月の範囲は営業日で切る</b>（→ <see cref="ITimeRecordRepository.ListByMonthAsync"/>）。
/// 打刻日時で切ると、深夜勤務が隣の月へこぼれる。
/// </summary>
public sealed class AttendanceQueryService(
    IStoreRepository stores, ITimeRecordRepository records) : IAttendanceQueryService
{
    public async Task<IReadOnlyList<AttendanceRow>> GetAsync(
        AttendanceQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var store = await stores.GetAsync(ct);

        if (store is null)
        {
            return [];
        }

        var found = await records.ListByMonthAsync(
            store.Id, query.Year, query.Month, query.StaffId, ct);

        var rows = AttendanceAggregator.SummarizeAll(found)
            .Where(summary => Matches(summary, query.Filter));

        return Sort(rows, query.Sort).Select(ToRow).ToList();
    }

    /// <summary>
    /// 表示順に並べ替える。
    ///
    /// 同じ営業日のなかは出勤の早い順（降順なら遅い順）にする。
    /// 並びが日付だけだと、同じ日の行の順序が取得方法に左右されて安定しない。
    /// </summary>
    private static IEnumerable<DailyAttendance> Sort(
        IEnumerable<DailyAttendance> rows, AttendanceSort sort)
        => sort == AttendanceSort.DateAscending
            ? rows.OrderBy(r => r.WorkDate).ThenBy(r => r.ClockInAt)
            : rows.OrderByDescending(r => r.WorkDate).ThenByDescending(r => r.ClockInAt);

    private static bool Matches(DailyAttendance summary, AttendanceFilter filter) => filter switch
    {
        AttendanceFilter.NeedsReview => summary.Status == AttendanceStatus.NeedsReview,
        AttendanceFilter.Incomplete => summary.IsIncomplete,
        _ => true,
    };

    /// <summary>
    /// 氏名は打刻に紐づくスタッフから取る。
    ///
    /// 別途スタッフを引き直さないのは、退職者の行も一覧に残す必要があるため。
    /// 在籍中だけを引くと、退職した人の過去の勤怠が氏名なしで並ぶ。
    /// </summary>
    private static AttendanceRow ToRow(DailyAttendance summary)
    {
        var staff = summary.Records.Select(r => r.Staff).FirstOrDefault(s => s is not null);

        return new AttendanceRow(
            summary,
            staff?.Name ?? "(不明なスタッフ)",
            staff?.StaffNo,
            staff?.EmploymentType);
    }
}
