namespace QTimeRecord.Core.Domain;

/// <summary>
/// ある営業日の、あるスタッフの現在の勤務状態。
///
/// <b>この値は DB に持たない。</b>打刻履歴から都度導出する。
/// 列に持つと、管理者が過去の打刻を修正したときに状態だけ古いまま残る。
/// </summary>
public enum PunchState
{
    /// <summary>その営業日にまだ打刻が無い。</summary>
    NotClockedIn,

    /// <summary>出勤済みで勤務中。</summary>
    Working,

    /// <summary>中抜け中（一時退出）。</summary>
    OnBreak,

    /// <summary>退勤済み。</summary>
    ClockedOut,
}

/// <summary>打刻要求をどう扱うか。</summary>
public enum PunchDecision
{
    /// <summary>正常な遷移。そのまま記録する。</summary>
    Normal,

    /// <summary>異常な遷移。確認したうえで記録する（→ plan.md Q7）。</summary>
    Warning,

    /// <summary>連打・二度読み。記録しない。</summary>
    Duplicate,
}

/// <summary>
/// 打刻要求の判定結果。
/// </summary>
/// <param name="Decision">扱い。</param>
/// <param name="Reason">
/// 異常・重複と判断した理由。スタッフに見せる文言で書く。正常なら null。
/// </param>
public sealed record PunchJudgement(PunchDecision Decision, string? Reason)
{
    public static readonly PunchJudgement Normal = new(PunchDecision.Normal, null);

    /// <summary>確認ダイアログを出す必要があるか。</summary>
    public bool NeedsConfirmation => Decision == PunchDecision.Warning;
}

/// <summary>
/// 打刻履歴から現在の状態を導き、次の打刻が正常か異常かを判定する。
///
/// <b>異常でも打刻をブロックしない。</b>現場で打刻できないことの損害のほうが大きく、
/// 誤りは管理者が後から修正できる（→ plan.md Q7 / CLAUDE.md 必須ルール2）。
/// 保存しないのは連打・二度読みだけ。
/// </summary>
public static class PunchStateMachine
{
    /// <summary>
    /// 同一種別の打刻を重複とみなす間隔。
    ///
    /// ボタンの連打と、リーダーが同じQRを2回読むケースを吸収する。
    /// これより長い間隔は「本人が意図して押した」とみなし、異常でも記録する。
    /// </summary>
    public static readonly TimeSpan DuplicateWindow = TimeSpan.FromSeconds(60);

    /// <summary>
    /// 打刻履歴から現在の状態を導く。
    ///
    /// 渡すのは<b>同じ営業日・同じスタッフ</b>の打刻に限ること。
    /// 異常な打刻も記録される仕様のため、履歴を畳んでも結局は最後の1件で決まる。
    /// </summary>
    public static PunchState Resolve(IEnumerable<TimeRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);

        var last = records.OrderBy(r => r.RecordedAt).LastOrDefault();

        return last is null ? PunchState.NotClockedIn : StateAfter(last.RecordType);
    }

    /// <summary>その打刻を記録したあとの状態。</summary>
    public static PunchState StateAfter(TimeRecordType type) => type switch
    {
        TimeRecordType.ClockIn => PunchState.Working,
        TimeRecordType.ClockOut => PunchState.ClockedOut,
        TimeRecordType.BreakStart => PunchState.OnBreak,
        TimeRecordType.BreakEnd => PunchState.Working,
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    /// <summary>
    /// 次の打刻をどう扱うか決める。
    /// </summary>
    /// <param name="state">現在の状態。</param>
    /// <param name="type">これから記録しようとしている打刻種別。</param>
    /// <param name="latest">
    /// 直近の打刻1件（営業日をまたいでもよい）。重複の判定に使う。無ければ null。
    /// 営業日で区切ると、日の境目をまたいだ二度読みを取り逃がす。
    /// </param>
    /// <param name="now">現在時刻。</param>
    public static PunchJudgement Evaluate(
        PunchState state, TimeRecordType type, TimeRecord? latest, DateTime now)
    {
        if (IsDuplicate(type, latest, now))
        {
            return new PunchJudgement(PunchDecision.Duplicate, "すでに打刻済みです。");
        }

        var reason = DescribeAnomaly(state, type);

        return reason is null ? PunchJudgement.Normal : new PunchJudgement(PunchDecision.Warning, reason);
    }

    private static bool IsDuplicate(TimeRecordType type, TimeRecord? latest, DateTime now)
    {
        if (latest is null || latest.RecordType != type)
        {
            return false;
        }

        var elapsed = now - latest.RecordedAt;

        // 未来の打刻（時計のずれや手修正）は重複とみなさない。
        // 無条件に弾くと、時計を戻した端末で一切打刻できなくなる。
        return elapsed >= TimeSpan.Zero && elapsed <= DuplicateWindow;
    }

    /// <summary>
    /// 異常な遷移なら理由を返す。正常なら null。
    ///
    /// 遷移表は plan.md 9-3 のとおり。
    /// 文言は<b>状態の説明だけ</b>にして、確認の問いかけは呼び出し側で組み立てる。
    /// </summary>
    private static string? DescribeAnomaly(PunchState state, TimeRecordType type) => (state, type) switch
    {
        (PunchState.NotClockedIn, TimeRecordType.ClockIn) => null,
        (PunchState.Working, TimeRecordType.ClockOut) => null,
        (PunchState.Working, TimeRecordType.BreakStart) => null,
        (PunchState.OnBreak, TimeRecordType.BreakEnd) => null,

        (PunchState.NotClockedIn, TimeRecordType.BreakEnd) => "中抜け開始の打刻がありません。",
        (PunchState.NotClockedIn, _) => "出勤の打刻がありません。",

        (PunchState.Working, TimeRecordType.ClockIn) => "すでに出勤しています。",
        (PunchState.Working, TimeRecordType.BreakEnd) => "中抜け開始の打刻がありません。",

        (PunchState.OnBreak, TimeRecordType.BreakStart) => "すでに中抜け中です。",
        (PunchState.OnBreak, _) => "中抜け中です。中抜け終了の打刻がありません。",

        // 退勤後の再出勤は運用としてあり得る（早番 → 中抜け → 遅番）。
        // 禁止せず、警告して記録する（→ plan.md Q8）。
        (PunchState.ClockedOut, _) => "すでに退勤しています。",

        _ => null,
    };
}
