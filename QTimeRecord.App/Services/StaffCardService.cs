using System.IO;
using Microsoft.Extensions.Logging;
using QTimeRecord.Core.Data.Repositories;
using QTimeRecord.Core.Domain;
using QTimeRecord.Core.Infrastructure;
using QTimeRecord.Core.Services;

namespace QTimeRecord.App.Services;

/// <summary>QR カードの画像と、そのもとになった発行日。</summary>
/// <param name="Png">カード画像（PNG）。</param>
/// <param name="IssuedOn">発行日。</param>
public sealed record StaffCard(byte[] Png, DateOnly IssuedOn);

public interface IStaffCardService
{
    /// <summary>そのスタッフの有効な QR からカード画像を作る。未発行なら null。</summary>
    Task<StaffCard?> RenderAsync(Staff staff, CancellationToken ct = default);

    /// <summary>カード画像をファイルに保存する。</summary>
    Task SaveAsync(byte[] png, string path, CancellationToken ct = default);

    /// <summary>保存時に提案するファイル名。</summary>
    string SuggestFileName(Staff staff);
}

/// <summary>
/// QR カードの組み立て。
///
/// トークン文字列はカードに<b>印字しない</b>（→ plan.md Q14）。
/// 印字すると、写真を撮るだけで他人が打刻できるようになる。
/// </summary>
public sealed class StaffCardService(
    IStoreRepository stores,
    IQrTokenRepository tokens,
    IQrCodeImageGenerator qrCodes,
    IQrCardRenderer cards,
    ILogger<StaffCardService> logger) : IStaffCardService
{
    public async Task<StaffCard?> RenderAsync(Staff staff, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(staff);

        var token = await tokens.GetActiveForStaffAsync(staff.Id, ct);
        var store = await stores.GetAsync(ct);

        if (token is null || store is null)
        {
            return null;
        }

        var png = cards.RenderPng(new QrCardContent
        {
            StoreName = store.StoreName,
            StaffName = staff.Name,
            StaffNo = staff.StaffNo,
            NameKana = staff.NameKana,
            IssuedOn = DateOnly.FromDateTime(token.IssuedAt),
            QrPng = qrCodes.CreatePng(token.Token),
        });

        return new StaffCard(png, DateOnly.FromDateTime(token.IssuedAt));
    }

    public async Task SaveAsync(byte[] png, string path, CancellationToken ct = default)
    {
        await File.WriteAllBytesAsync(path, png, ct);

        // ファイル名にも氏名が入る。パスはログに残さない（→ CLAUDE.md 必須ルール6）。
        logger.LogInformation("QR カードを保存しました。");
    }

    public string SuggestFileName(Staff staff)
    {
        ArgumentNullException.ThrowIfNull(staff);

        var label = string.IsNullOrWhiteSpace(staff.StaffNo) ? staff.Name : staff.StaffNo;

        return $"QRカード_{Sanitize(label)}.png";
    }

    /// <summary>ファイル名に使えない文字を落とす。氏名に記号が入っていても保存できるように。</summary>
    private static string Sanitize(string value)
        => string.Concat(value.Where(c => !Path.GetInvalidFileNameChars().Contains(c))).Trim();
}
