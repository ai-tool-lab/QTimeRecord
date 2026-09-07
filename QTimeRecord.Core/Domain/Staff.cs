namespace QTimeRecord.Core.Domain;

/// <summary>
/// スタッフ。
///
/// <b>物理削除しない。</b> 打刻レコードが参照しているため、
/// 削除すると過去の勤務記録が壊れる。退職は <see cref="Status"/> で表す。
/// </summary>
public sealed class Staff
{
    public required Guid Id { get; set; }

    public required Guid StoreId { get; set; }

    /// <summary>社員番号（例: E-0104）。CSV で給与システムと突き合わせるときのキー。</summary>
    public string? StaffNo { get; set; }

    public required string Name { get; set; }

    /// <summary>フリガナ。名簿の並び替えと検索に使う。</summary>
    public string? NameKana { get; set; }

    /// <summary>区分（社員／アルバイト／パート）。集計はせず、表示と絞り込みのみに使う。</summary>
    public string? EmploymentType { get; set; }

    public required StaffStatus Status { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public Store? Store { get; set; }

    public List<StaffQrToken> QrTokens { get; } = [];
}

/// <summary>
/// スタッフに発行した QR トークン。
///
/// 再発行に備えて履歴として持つ。有効なのは <see cref="RevokedAt"/> が null のもの。
/// スタッフ側に「現在のトークン」を持たせない設計にしているのは、
/// 失効と発行を1つのテーブルの操作だけで完結させるため。
/// </summary>
public sealed class StaffQrToken
{
    public required Guid Id { get; set; }

    public required Guid StoreId { get; set; }

    public required Guid StaffId { get; set; }

    /// <summary>
    /// QR に埋め込む文字列。128bit の乱数を Base32 で表現した32文字。
    /// 氏名・社員番号など推測できる情報は含めない。
    /// </summary>
    public required string Token { get; set; }

    public required DateTime IssuedAt { get; set; }

    /// <summary>失効日時。null なら有効。</summary>
    public DateTime? RevokedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public Staff? Staff { get; set; }

    public bool IsActive => RevokedAt is null;
}
