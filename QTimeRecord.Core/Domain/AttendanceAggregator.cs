namespace QTimeRecord.Core.Domain;

/// <summary>
/// 勤務状況一覧に出すバッジ。1行につき1つ。
///
/// 複数当てはまるときは<b>要確認を優先する</b>。管理者が最初に直すべき行だから。
/// </summary>
public enum AttendanceStatus
{
    /// <summary>QR 打刻のみで、遷移も揃っている。</summary>
    Normal,

    /// <summary>管理者が手で追加した打刻を含む。</summary>
    ManualAdd,

    /// <summary>管理者が手で直した打刻を含む。</summary>
    ManualEdit,

    /// <summary>出勤・退勤の欠け、または異常な遷移がある。</summary>
    NeedsReview,
}

/// <summary>
/// スタッフ1名・営業日1日ぶんの集計。一覧の1行にあたる。
///
/// <b>この型は保存しない。</b>打刻から都度導出する。
/// 保存すると、管理者が打刻を直したときに集計だけ古いまま残る。
/// </summary>
public sealed record DailyAttendance
{
    public required Guid StaffId { get; init; }

    /// <summary>営業日。カレンダー日付ではない（→ CLAUDE.md 必須ルール1）。</summary>
    public required DateOnly WorkDate { get; init; }

    /// <summary>その日の最初の出勤。無ければ null。</summary>
    public DateTime? ClockInAt { get; init; }

    /// <summary>その日の最後の退勤。無ければ null。</summary>
    public DateTime? ClockOutAt { get; init; }

    /// <summary>最初の中抜け開始。</summary>
    public DateTime? BreakStartAt { get; init; }

    /// <summary>最後の中抜け終了。</summary>
    public DateTime? BreakEndAt { get; init; }

    /// <summary>開始と終了が対応した中抜けの回数（→ plan.md Q3）。</summary>
    public int BreakCount { get; init; }

    /// <summary>対応が取れた中抜けの合計（分）。</summary>
    public int BreakMinutes { get; init; }

    /// <summary>実労働（分）。出勤か退勤が欠けていれば null。</summary>
    public int? WorkedMinutes { get; init; }

    public bool HasManualAdd { get; init; }

    public bool HasManualEdit { get; init; }

    /// <summary>
    /// 要確認にした理由。空なら要確認ではない。
    ///
    /// <b>理由を持たない「要確認」を作らない。</b>バッジだけ出しても、
    /// 一覧には最初の出勤と最後の退勤しか出ていないため、
    /// 管理者はどの打刻を直せばよいのか分からない（2026-09-09 の申告）。
    /// </summary>
    public required IReadOnlyList<string> ReviewReasons { get; init; }

    /// <summary>出勤・退勤の欠け、対応の取れない中抜け、異常な遷移のいずれかがある。</summary>
    public bool NeedsReview => ReviewReasons.Count > 0;

    /// <summary>出勤か退勤が欠けている。「打刻漏れ」の絞り込みに使う。</summary>
    public bool IsIncomplete => ClockInAt is null || ClockOutAt is null;

    /// <summary>修正理由のメモ。最後に書かれたものを出す（→ plan.md Q20）。</summary>
    public string? Note { get; init; }

    /// <summary>この行の打刻。編集ダイアログへ渡す。</summary>
    public required IReadOnlyList<TimeRecord> Records { get; init; }

    public AttendanceStatus Status => this switch
    {
        { NeedsReview: true } => AttendanceStatus.NeedsReview,
        { HasManualAdd: true } => AttendanceStatus.ManualAdd,
        { HasManualEdit: true } => AttendanceStatus.ManualEdit,
        _ => AttendanceStatus.Normal,
    };
}

/// <summary>
/// 打刻を営業日ごとに畳んで一覧の行にする。
///
/// 異常の判定は <see cref="PunchStateMachine"/> を通す。ここで別の規則を書くと、
/// 打刻時に警告した内容と一覧の「要確認」がずれる。
/// </summary>
public static class AttendanceAggregator
{
    /// <summary>
    /// スタッフ1名・営業日1日ぶんの打刻を畳む。
    ///
    /// 打刻が欠けていても例外にしない。欠けていること自体が一覧に出すべき情報で、
    /// ここで落とすと管理者が直せなくなる。
    /// </summary>
    public static DailyAttendance Summarize(
        Guid staffId, DateOnly workDate, IEnumerable<TimeRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);

        var ordered = records.OrderBy(r => r.RecordedAt).ToList();

        var clockIn = ordered.FirstOrDefault(r => r.RecordType == TimeRecordType.ClockIn);
        var clockOut = ordered.LastOrDefault(r => r.RecordType == TimeRecordType.ClockOut);

        var breaks = PairBreaks(ordered);

        // 退勤が出勤より前になっている日は、実労働を計算しない。
        // 0 として出すと「0時間働いた」ように見え、打刻が壊れていることが伝わらない。
        var worked = clockIn is not null
            && clockOut is not null
            && clockOut.RecordedAt >= clockIn.RecordedAt
            ? (int)(clockOut.RecordedAt - clockIn.RecordedAt).TotalMinutes - breaks.Minutes
            : (int?)null;

        return new DailyAttendance
        {
            StaffId = staffId,
            WorkDate = workDate,
            ClockInAt = clockIn?.RecordedAt,
            ClockOutAt = clockOut?.RecordedAt,
            BreakStartAt = ordered
                .FirstOrDefault(r => r.RecordType == TimeRecordType.BreakStart)?.RecordedAt,
            BreakEndAt = ordered
                .LastOrDefault(r => r.RecordType == TimeRecordType.BreakEnd)?.RecordedAt,
            BreakCount = breaks.Count,
            BreakMinutes = breaks.Minutes,

            // 中抜けが実労働を超えるのは打刻が壊れている場合だけ。負の時間は出さない。
            WorkedMinutes = worked is null ? null : Math.Max(worked.Value, 0),

            HasManualAdd = ordered.Any(r => r.EntryMethod == EntryMethod.ManualAdd),
            HasManualEdit = ordered.Any(r => r.EntryMethod == EntryMethod.ManualEdit),
            ReviewReasons = ReviewReasons(ordered, clockIn, clockOut, breaks.HasUnclosed),
            Note = ordered.LastOrDefault(r => !string.IsNullOrWhiteSpace(r.Note))?.Note,
            Records = ordered,
        };
    }

    /// <summary>
    /// 打刻を営業日 × スタッフで畳む。一覧はこの単位で並ぶ。
    ///
    /// 営業日の昇順で返す。<b>これは安定した既定の並びで、画面に出す順ではない。</b>
    /// 表示順は <c>AttendanceQueryService</c> が指定に応じて決める。
    /// </summary>
    public static IReadOnlyList<DailyAttendance> SummarizeAll(IEnumerable<TimeRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);

        return records
            .GroupBy(r => (r.StaffId, r.WorkDate))
            .Select(group => Summarize(group.Key.StaffId, group.Key.WorkDate, group))
            .OrderBy(row => row.WorkDate)
            .ThenBy(row => row.ClockInAt)
            .ToList();
    }

    /// <summary>
    /// 中抜けの開始と終了を対応付ける。
    ///
    /// 開始だけで終わっている分は合計に入れない。終了時刻が分からないものを
    /// 「いま現在まで」として数えると、翌月になっても増え続ける。
    /// </summary>
    private static (int Count, int Minutes, bool HasUnclosed) PairBreaks(
        IReadOnlyList<TimeRecord> ordered)
    {
        DateTime? openedAt = null;
        var count = 0;
        var minutes = 0;

        foreach (var record in ordered)
        {
            switch (record.RecordType)
            {
                case TimeRecordType.BreakStart when openedAt is null:
                    openedAt = record.RecordedAt;
                    break;

                case TimeRecordType.BreakEnd when openedAt is not null:
                    minutes += (int)Math.Max((record.RecordedAt - openedAt.Value).TotalMinutes, 0);
                    count++;
                    openedAt = null;
                    break;
            }
        }

        return (count, minutes, openedAt is not null);
    }

    /// <summary>
    /// 要確認にする理由をすべて挙げる。無ければ空。
    ///
    /// 実労働の長さは理由にしない。12時間勤務も夜勤も正常な打刻であり、
    /// 長さの妥当性は勤怠の締めで見るもので、打刻の壊れとは別の話。
    /// </summary>
    private static IReadOnlyList<string> ReviewReasons(
        IReadOnlyList<TimeRecord> ordered,
        TimeRecord? clockIn,
        TimeRecord? clockOut,
        bool hasUnclosedBreak)
    {
        if (ordered.Count == 0)
        {
            return [];
        }

        var reasons = new List<string>();

        if (clockIn is null)
        {
            reasons.Add("出勤の打刻がありません。");
        }

        if (clockOut is null)
        {
            reasons.Add("退勤の打刻がありません。");
        }

        if (clockIn is not null && clockOut is not null && clockOut.RecordedAt < clockIn.RecordedAt)
        {
            reasons.Add("退勤が出勤より前になっています。");
        }

        if (hasUnclosedBreak)
        {
            reasons.Add("中抜け終了の打刻がありません。");
        }

        reasons.AddRange(Anomalies(ordered));

        // 同じ理由が打刻ごとに何度も並ぶと読めない。
        return reasons.Distinct().ToList();
    }

    /// <summary>
    /// 異常な遷移の理由。打刻時と同じ判定を通す。
    /// 重複の判定は使わない（保存済みのものは重複ではない）。
    /// </summary>
    private static IEnumerable<string> Anomalies(IReadOnlyList<TimeRecord> ordered)
    {
        var state = PunchState.NotClockedIn;

        foreach (var record in ordered)
        {
            var judgement = PunchStateMachine.Evaluate(
                state, record.RecordType, latest: null, record.RecordedAt);

            if (judgement is { Decision: PunchDecision.Warning, Reason: { } reason })
            {
                yield return reason;
            }

            state = PunchStateMachine.StateAfter(record.RecordType);
        }
    }
}
