namespace QTimeRecord.Core.Domain;

/// <summary>営業日の境界。店舗設定から作る。</summary>
public sealed record BusinessDaySettings(TimeOnly Start, TimeOnly End)
{
    public static BusinessDaySettings From(Store store) => new(store.BusinessDayStart, store.BusinessDayEnd);

    /// <summary>開始と終了が同じなら24時間営業とみなす。</summary>
    public bool IsAroundTheClock => Start == End;

    /// <summary>終了が開始より前なら、営業日が日をまたぐ（例 11:00〜翌05:00）。</summary>
    public bool CrossesMidnight => End < Start;
}

/// <summary>
/// 打刻日時から営業日を決める。
///
/// <b>営業日の導出はこのクラスだけが行う。</b>
/// 打刻・日次集計・CSV のすべてがここを通す。他所で <c>recordedAt.Date</c> を書いた時点でバグになる。
///
/// 判定規則（→ plan.md Q2 / Q2-a）
/// <code>
/// 営業時間内           → 打刻時刻 >= 開始時刻 なら その日、そうでなければ 前日
/// 営業時間外（閉店中） → 直近の出勤が未退勤 かつ その出勤から18時間以内 → 前営業日
///                        それ以外                                     → 当日
/// </code>
/// </summary>
public static class BusinessDayResolver
{
    /// <summary>
    /// 「まだ勤務中」とみなす上限。
    ///
    /// 上限が無いと、<b>前日に退勤を忘れた人が翌朝出勤したときに前営業日へ付いてしまう</b>。
    /// 退勤忘れは現場で頻繁に起きる。深夜勤務の延長は長くても十数時間で収まる一方、
    /// 退勤忘れは前日の出勤から20時間以上経っていることがほとんどなので、この線で切り分けられる。
    /// </summary>
    public const int OpenShiftGraceHours = 18;

    /// <summary>
    /// 営業日を決める。
    /// </summary>
    /// <param name="recordedAt">打刻日時。</param>
    /// <param name="settings">店舗の営業日設定。</param>
    /// <param name="openClockInAt">
    /// 直近の未退勤の出勤日時。退勤済み、または出勤自体が無ければ null。
    /// 営業時間外の打刻でのみ参照する。
    /// </param>
    public static DateOnly Resolve(
        DateTime recordedAt, BusinessDaySettings settings, DateTime? openClockInAt = null)
    {
        var calendarDate = DateOnly.FromDateTime(recordedAt);
        var timeOfDay = TimeOnly.FromDateTime(recordedAt);

        // 開始時刻より前なら、まだ前日の営業日が続いている。
        var shiftedDate = timeOfDay >= settings.Start ? calendarDate : calendarDate.AddDays(-1);

        if (IsWithinBusinessHours(recordedAt, settings))
        {
            return shiftedDate;
        }

        // ここから閉店中。深夜勤務の延長か、翌営業日の準備かを勤務状態で見分ける。
        return IsContinuingShift(recordedAt, openClockInAt) ? shiftedDate : calendarDate;
    }

    /// <summary>営業時間内か。営業日の決定には使わず、時間外の打刻を警告するために使う。</summary>
    public static bool IsWithinBusinessHours(DateTime recordedAt, BusinessDaySettings settings)
    {
        if (settings.IsAroundTheClock)
        {
            return true;
        }

        var timeOfDay = TimeOnly.FromDateTime(recordedAt);

        return settings.CrossesMidnight
            ? timeOfDay >= settings.Start || timeOfDay < settings.End
            : timeOfDay >= settings.Start && timeOfDay < settings.End;
    }

    private static bool IsContinuingShift(DateTime recordedAt, DateTime? openClockInAt)
    {
        if (openClockInAt is null)
        {
            return false;
        }

        var elapsed = recordedAt - openClockInAt.Value;

        // 未来の出勤（時計のずれや手修正）は勤務継続とみなさない。
        return elapsed >= TimeSpan.Zero && elapsed <= TimeSpan.FromHours(OpenShiftGraceHours);
    }
}
