using System.Security.Cryptography;
using System.Text;
using QTimeRecord.Core.Data.Repositories;
using QTimeRecord.Core.Domain;
using QTimeRecord.Core.Infrastructure;

namespace QTimeRecord.Core.Services;

/// <summary>QR を読んだ結果。</summary>
public enum QrResolution
{
    /// <summary>有効な QR で、打刻できるスタッフ。</summary>
    Ok,

    /// <summary>登録されていない QR。</summary>
    NotFound,

    /// <summary>再発行などで失効した QR。</summary>
    Revoked,

    /// <summary>休職中のスタッフ。</summary>
    OnLeave,

    /// <summary>退職済みのスタッフ。</summary>
    Retired,
}

/// <summary>QR の解決結果。</summary>
/// <param name="Resolution">結果の種別。</param>
/// <param name="Staff">特定できたスタッフ。特定できなければ null。</param>
/// <param name="StaffId">
/// スタッフ ID。<see cref="Staff"/> を読み込まない場合（失効など）でも、
/// ログに残せるようここに入れる（→ plan.md 15-1）。
/// </param>
/// <param name="RevokedAt">失効日時。失効している場合のみ。</param>
public sealed record QrResolveResult(
    QrResolution Resolution, Staff? Staff, Guid? StaffId = null, DateTime? RevokedAt = null)
{
    public bool CanPunch => Resolution == QrResolution.Ok;

    public static QrResolveResult Failed(QrResolution resolution) => new(resolution, null);
}

public interface IQrTokenService
{
    /// <summary>新しいトークン文字列を作る。</summary>
    string GenerateToken();

    /// <summary>読み取った文字列からスタッフを特定する。</summary>
    Task<QrResolveResult> ResolveAsync(string token, CancellationToken ct = default);

    /// <summary>スタッフへ QR を発行する（既存は失効させる）。</summary>
    Task<StaffQrToken> IssueAsync(Guid storeId, Guid staffId, CancellationToken ct = default);

    /// <summary>有効な QR をすべて失効させる。</summary>
    Task<int> RevokeAsync(Guid staffId, CancellationToken ct = default);
}

public sealed class QrTokenService(IQrTokenRepository tokens, IClock clock) : IQrTokenService
{
    /// <summary>
    /// トークンの長さ（文字数）。20バイト＝160bit を Base32 にすると端数なく32文字になる。
    /// </summary>
    public const int TokenLength = 32;

    private const int RandomBytes = 20;

    /// <summary>
    /// RFC 4648 の Base32。数字の 0/1/8 と紛らわしい英字を含まないため、
    /// 読み取り失敗時に人が目視で確認しやすい。
    /// </summary>
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public string GenerateToken()
    {
        // 推測できてはいけない。連番や社員番号由来にすると、他人のQRを作れてしまう。
        var bytes = RandomNumberGenerator.GetBytes(RandomBytes);

        return ToBase32(bytes);
    }

    public async Task<QrResolveResult> ResolveAsync(string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return QrResolveResult.Failed(QrResolution.NotFound);
        }

        var found = await tokens.FindByTokenAsync(token.Trim(), ct);

        if (found is null)
        {
            return QrResolveResult.Failed(QrResolution.NotFound);
        }

        // 「登録されていない」と「無効になった」は案内が違う。ここで区別する。
        if (!found.IsActive)
        {
            return new QrResolveResult(
                QrResolution.Revoked, Staff: null, found.StaffId, found.RevokedAt);
        }

        var staff = found.Staff;
        if (staff is null)
        {
            return QrResolveResult.Failed(QrResolution.NotFound);
        }

        return staff.Status switch
        {
            StaffStatus.Active => new QrResolveResult(QrResolution.Ok, staff),
            StaffStatus.OnLeave => new QrResolveResult(QrResolution.OnLeave, staff),
            StaffStatus.Retired => new QrResolveResult(QrResolution.Retired, staff),
            _ => QrResolveResult.Failed(QrResolution.NotFound),
        };
    }

    public Task<StaffQrToken> IssueAsync(Guid storeId, Guid staffId, CancellationToken ct = default)
        => tokens.IssueAsync(storeId, staffId, GenerateToken(), clock.Now, ct);

    public Task<int> RevokeAsync(Guid staffId, CancellationToken ct = default)
        => tokens.RevokeActiveAsync(staffId, clock.Now, ct);

    private static string ToBase32(ReadOnlySpan<byte> bytes)
    {
        var builder = new StringBuilder(TokenLength);

        var buffer = 0;
        var bitsLeft = 0;

        foreach (var b in bytes)
        {
            buffer = (buffer << 8) | b;
            bitsLeft += 8;

            while (bitsLeft >= 5)
            {
                bitsLeft -= 5;
                builder.Append(Alphabet[(buffer >> bitsLeft) & 0x1F]);
            }
        }

        // 20バイトなら端数は出ない。将来長さを変えたときのための保険。
        if (bitsLeft > 0)
        {
            builder.Append(Alphabet[(buffer << (5 - bitsLeft)) & 0x1F]);
        }

        return builder.ToString();
    }
}
