namespace QTimeRecord.Core.Domain;

/// <summary>
/// QR リーダー（シリアルポート）の接続設定。店舗ごとに1行。
///
/// appsettings.json ではなく DB に置く理由は plan.md 13-1 を参照。
/// </summary>
public sealed class DeviceSettings
{
    public required Guid StoreId { get; set; }

    /// <summary>例: COM3。未設定なら null。</summary>
    public string? ComPort { get; set; }

    public int BaudRate { get; set; } = 9600;

    public int DataBits { get; set; } = 8;

    /// <summary>System.IO.Ports.Parity の名前を保存する（None / Odd / Even / Mark / Space）。</summary>
    public string Parity { get; set; } = "None";

    /// <summary>System.IO.Ports.StopBits の名前を保存する（One / Two / OnePointFive）。</summary>
    public string StopBits { get; set; } = "One";

    public DateTime UpdatedAt { get; set; }

    public Store? Store { get; set; }
}

/// <summary>
/// 管理者のパスワード（8桁の数字）。店舗ごとに1つで、複数アカウントは持たない。
///
/// <b>この方式は「端末を触れる人の誤操作を防ぐ」もので、盗まれた DB を守るものではない。</b>
/// 8桁数字は1億通りしかなく、ハッシュが流出すればオフラインでの総当たりは現実的に可能。
/// DB ファイル自体の保護（端末の物理管理・ディスク暗号化）が前提になる。
/// </summary>
public sealed class AdminCredential
{
    public required Guid StoreId { get; set; }

    /// <summary>PBKDF2-HMAC-SHA256 の導出鍵（Base64）。</summary>
    public required string PasswordHash { get; set; }

    /// <summary>ソルト16バイト（Base64）。</summary>
    public required string Salt { get; set; }

    /// <summary>反復回数。将来引き上げたときに既存のハッシュを検証できるよう、行ごとに持つ。</summary>
    public required int Iterations { get; set; }

    /// <summary>連続失敗回数。認証に成功したら 0 に戻す。</summary>
    public int FailedCount { get; set; }

    /// <summary>
    /// ロック解除時刻。null ならロックされていない。
    ///
    /// メモリではなく DB に置くのは、アプリを再起動するだけでロックを回避されないようにするため。
    /// </summary>
    public DateTime? LockedUntil { get; set; }

    public DateTime UpdatedAt { get; set; }

    public Store? Store { get; set; }
}
