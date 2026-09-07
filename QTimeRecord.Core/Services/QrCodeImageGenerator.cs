using QRCoder;

namespace QTimeRecord.Core.Services;

public interface IQrCodeImageGenerator
{
    /// <summary>QR コードを PNG のバイト列として作る。</summary>
    byte[] CreatePng(string content, int pixelsPerModule = 10);
}

/// <summary>
/// QR コードの画像化。
///
/// 描画ライブラリに依存しない <see cref="PngByteQRCode"/> を使う。
/// カードへの合成は表示側（App）で行い、Core は画像データだけを返す。
///
/// クラス名を QRCoder の <c>QRCodeGenerator</c> と紛らわしくしないため
/// <c>Image</c> を挟んでいる。
/// </summary>
public sealed class QrCodeImageGenerator : IQrCodeImageGenerator
{
    /// <summary>
    /// 誤り訂正レベル。
    ///
    /// カードは財布やポケットで擦れ、汚れる。既定の M（15%）ではなく Q（25%）にして、
    /// 多少傷んでも読めるようにする。トークンは32文字と短く、レベルを上げても
    /// QR の型番はほとんど大きくならない。
    /// </summary>
    private const QRCodeGenerator.ECCLevel ErrorCorrection = QRCodeGenerator.ECCLevel.Q;

    public byte[] CreatePng(string content, int pixelsPerModule = 10)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        ArgumentOutOfRangeException.ThrowIfLessThan(pixelsPerModule, 1);

        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(content, ErrorCorrection);

        return new PngByteQRCode(data).GetGraphic(pixelsPerModule);
    }
}
