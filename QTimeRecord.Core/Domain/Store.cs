namespace QTimeRecord.Core.Domain;

/// <summary>
/// 店舗。
///
/// 1端末＝1店舗のため、このテーブルは常に1行だけ持つ。
/// それでも他のテーブルに <c>StoreId</c> を持たせているのは、
/// 将来サーバーへ集約したときに店舗をまたいで束ねられるようにするため。
/// </summary>
public sealed class Store
{
    public required Guid Id { get; set; }

    /// <summary>人が読む店舗コード（例: 101）。主キーには使わない。</summary>
    public string? StoreCode { get; set; }

    public required string StoreName { get; set; }

    /// <summary>法人・会社名。店舗設定では読み取り専用として表示する。</summary>
    public required string CompanyName { get; set; }

    /// <summary>お知らせの見出し（最大40文字）。</summary>
    public string? AnnouncementTitle { get; set; }

    /// <summary>
    /// 1日の開始時刻。営業日の境界はこれだけで決まる。
    /// 例: 11:00 なら、翌 02:00 の打刻は前日の営業日に属する。
    /// </summary>
    public required TimeOnly BusinessDayStart { get; set; }

    /// <summary>
    /// 1日の終了時刻。営業日の決定には使わず、
    /// 「営業時間外（閉店中）の打刻か」の判定に使う。
    /// </summary>
    public required TimeOnly BusinessDayEnd { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public List<Announcement> Announcements { get; } = [];
}

/// <summary>
/// メイン画面に表示するお知らせの1項目。1店舗あたり最大5件。
/// </summary>
public sealed class Announcement
{
    public required Guid Id { get; set; }

    public required Guid StoreId { get; set; }

    /// <summary>表示順（1〜5）。</summary>
    public required int DisplayOrder { get; set; }

    /// <summary>見出し（例: 健康診断）。最大30文字。</summary>
    public required string Heading { get; set; }

    /// <summary>本文。最大200文字。</summary>
    public required string Body { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public Store? Store { get; set; }
}
