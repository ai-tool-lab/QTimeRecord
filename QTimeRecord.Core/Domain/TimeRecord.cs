namespace QTimeRecord.Core.Domain;

/// <summary>
/// 打刻1件。
///
/// <see cref="RecordedAt"/> は実際に打刻した日時、
/// <see cref="WorkDate"/> は店舗設定の営業日境界で決めた「営業日」。
/// この2つは日付がずれることがある（深夜勤務）。<b>片方から他方を導かないこと。</b>
/// </summary>
public sealed class TimeRecord
{
    public required Guid Id { get; set; }

    public required Guid StoreId { get; set; }

    public required Guid StaffId { get; set; }

    public required TimeRecordType RecordType { get; set; }

    /// <summary>打刻日時。オフセット付きで保存する。</summary>
    public required DateTime RecordedAt { get; set; }

    /// <summary>
    /// 営業日。カレンダー日付ではない。
    ///
    /// 保存時に <c>BusinessDayResolver</c> で確定させ、以後は自動で再計算しない。
    /// 都度計算にすると、店舗設定の開始時刻を変えた瞬間に確定済みの過去月がずれる。
    /// </summary>
    public required DateOnly WorkDate { get; set; }

    public required EntryMethod EntryMethod { get; set; }

    /// <summary>
    /// 管理者が手動登録・修正したときの理由メモ。
    /// 修正履歴は保持しない仕様のため、最新の1件だけをここに持つ。
    /// </summary>
    public string? Note { get; set; }

    /// <summary>
    /// 営業時間外（閉店中）の打刻だったか。
    /// 勤務状況の一覧で「要確認」として色分けするために使う。
    /// </summary>
    public bool IsOutsideBusinessHours { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public Staff? Staff { get; set; }
}
