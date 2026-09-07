namespace QTimeRecord.Core.Domain;

/// <summary>
/// スタッフの在籍状態。
///
/// 退職しても行は消さない（打刻レコードが参照しているため）。
/// 打刻できるのは <see cref="Active"/> のみ。
/// </summary>
public enum StaffStatus
{
    /// <summary>在職中。打刻できる。</summary>
    Active,

    /// <summary>休職中。打刻するとエラーになる。</summary>
    OnLeave,

    /// <summary>退職済。打刻するとエラーになり、QR も失効させる。</summary>
    Retired,
}

/// <summary>
/// 打刻種別。
///
/// 通常の休憩は打刻しない（給与側で自動控除される前提）。
/// ここで扱うのは「中抜け」＝一時退出のみ。
/// </summary>
public enum TimeRecordType
{
    /// <summary>出勤（始業）。</summary>
    ClockIn,

    /// <summary>退勤（終業）。</summary>
    ClockOut,

    /// <summary>中抜け開始（一時退出）。</summary>
    BreakStart,

    /// <summary>中抜け終了（業務復帰）。</summary>
    BreakEnd,
}

/// <summary>
/// 打刻の登録方法。
///
/// 勤務状況の一覧で色分けし、手が入った打刻を管理者が見分けられるようにする。
/// </summary>
public enum EntryMethod
{
    /// <summary>QR コードによる本人の打刻。</summary>
    Qr,

    /// <summary>管理者による手動登録（打刻漏れの補填）。</summary>
    ManualAdd,

    /// <summary>管理者による修正（既存の打刻の上書き）。</summary>
    ManualEdit,
}
