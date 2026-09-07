using System.IO;
using System.Threading;
using QTimeRecord.App.Services;
using QTimeRecord.Core.Services;

namespace QTimeRecord.Tests;

/// <summary>
/// 目視確認用のカード画像を書き出す。通常のテスト実行では動かさない。
/// 実行するには:
///   dotnet test --filter "FullyQualifiedName~SampleCardGenerator"
/// </summary>
public sealed class SampleCardGenerator
{
    [Fact(Skip = "目視確認用。必要なときだけ Skip を外して実行する。")]
    public void カード画像を書き出す()
    {
        var output = Path.Combine(Path.GetTempPath(), "qtimerecord-sample-card.png");

        var thread = new Thread(() =>
        {
            var qr = new QrCodeImageGenerator().CreatePng("ABCDEFGHIJKLMNOPQRSTUVWXYZ234567");

            var png = new QrCardRenderer().RenderPng(new QrCardContent
            {
                StoreName = "相模原店",
                StaffName = "山田 太郎",
                NameKana = "ヤマダ タロウ",
                StaffNo = "E-0104",
                IssuedOn = new DateOnly(2026, 9, 6),
                QrPng = qr,
            });

            File.WriteAllBytes(output, png);
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.True(File.Exists(output));
    }
}
