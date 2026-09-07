using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QTimeRecord.Core.Services;

namespace QTimeRecord.App.Services;

/// <summary>カードに載せる内容。トークン文字列は載せない（→ plan.md Q14）。</summary>
public sealed record QrCardContent
{
    public required string StoreName { get; init; }

    public required string StaffName { get; init; }

    public string? StaffNo { get; init; }

    public string? NameKana { get; init; }

    public required DateOnly IssuedOn { get; init; }

    /// <summary>埋め込む QR の PNG。</summary>
    public required byte[] QrPng { get; init; }
}

public interface IQrCardRenderer
{
    /// <summary>名刺サイズのカード画像を PNG で作る。</summary>
    byte[] RenderPng(QrCardContent content);
}

/// <summary>
/// QR カードの描画。
///
/// WPF の描画をそのまま使う。System.Drawing を持ち込まずに済み、
/// 画面と同じフォント・同じ色で描ける。
/// </summary>
public sealed class QrCardRenderer : IQrCardRenderer
{
    /// <summary>名刺サイズ 91×55mm を 300dpi で。印刷しても粗くならない解像度。</summary>
    private const double Dpi = 300;
    private const double WidthMm = 91;
    private const double HeightMm = 55;

    private static readonly int PixelWidth = (int)Math.Round(WidthMm / 25.4 * Dpi);
    private static readonly int PixelHeight = (int)Math.Round(HeightMm / 25.4 * Dpi);

    public byte[] RenderPng(QrCardContent content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var visual = new DrawingVisual();

        using (var context = visual.RenderOpen())
        {
            Draw(context, content);
        }

        var bitmap = new RenderTargetBitmap(PixelWidth, PixelHeight, Dpi, Dpi, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var stream = new MemoryStream();
        encoder.Save(stream);

        return stream.ToArray();
    }

    private static void Draw(DrawingContext context, QrCardContent content)
    {
        // RenderTargetBitmap は DIP 単位で描く。dpi を上げた分だけ座標系は元のままでよい。
        var width = PixelWidth * 96.0 / Dpi;
        var height = PixelHeight * 96.0 / Dpi;

        var background = LookupBrush("Brush.SurfaceContainerLowest", Brushes.White);
        var border = LookupBrush("Brush.OutlineVariant", Brushes.LightGray);
        var primary = LookupBrush("Brush.Primary", Brushes.Black);
        var muted = LookupBrush("Brush.OnSurfaceVariant", Brushes.DimGray);

        context.DrawRectangle(background, new Pen(border, 1), new Rect(0, 0, width, height));

        const double margin = 18;

        // QR は右側に正方形で配置する。カードの高さから上下の余白を引いた大きさ。
        var qrSize = height - (margin * 2);
        var qr = LoadQr(content.QrPng);
        context.DrawImage(qr, new Rect(width - margin - qrSize, margin, qrSize, qrSize));

        var textWidth = width - qrSize - (margin * 3);
        var x = margin;
        var y = margin;

        y += DrawText(context, content.StoreName, 9, muted, x, y, textWidth);
        y += 6;

        if (!string.IsNullOrWhiteSpace(content.NameKana))
        {
            y += DrawText(context, content.NameKana, 8, muted, x, y, textWidth);
        }

        y += DrawText(context, content.StaffName, 18, primary, x, y, textWidth, bold: true);
        y += 8;

        if (!string.IsNullOrWhiteSpace(content.StaffNo))
        {
            y += DrawText(context, content.StaffNo, 10, muted, x, y, textWidth);
        }

        // 発行日は再発行の管理に使う。どのカードが最新かを目で判断できるようにする。
        DrawText(
            context,
            $"発行日: {content.IssuedOn:yyyy/MM/dd}",
            8,
            muted,
            x,
            height - margin - 12,
            textWidth);
    }

    private static double DrawText(
        DrawingContext context,
        string text,
        double fontSize,
        Brush brush,
        double x,
        double y,
        double maxWidth,
        bool bold = false)
    {
        var typeface = new Typeface(
            LookupFontFamily(),
            FontStyles.Normal,
            bold ? FontWeights.Bold : FontWeights.Normal,
            FontStretches.Normal);

        var formatted = new FormattedText(
            text,
            CultureInfo.GetCultureInfo("ja-JP"),
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            brush,
            pixelsPerDip: Dpi / 96.0)
        {
            MaxTextWidth = maxWidth,
            // 氏名が長くても QR に重ならないよう、1行に収めて省略する。
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
        };

        context.DrawText(formatted, new Point(x, y));

        return formatted.Height;
    }

    private static BitmapSource LoadQr(byte[] png)
    {
        using var stream = new MemoryStream(png);

        var decoder = new PngBitmapDecoder(
            stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);

        return decoder.Frames[0];
    }

    /// <summary>
    /// 画面と同じ配色を使う。アプリのリソースが無い状況（テスト）では既定色に落とす。
    /// </summary>
    private static Brush LookupBrush(string key, Brush fallback)
        => Application.Current?.TryFindResource(key) as Brush ?? fallback;

    private static FontFamily LookupFontFamily()
        => Application.Current?.TryFindResource("Font.Ui") as FontFamily
            ?? new FontFamily("Yu Gothic UI, Meiryo UI, Segoe UI");
}
