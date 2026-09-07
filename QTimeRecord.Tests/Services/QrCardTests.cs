using System.IO;
using System.Threading;
using QTimeRecord.App.Services;
using QTimeRecord.Core.Services;

namespace QTimeRecord.Tests.Services;

public sealed class QrCodeImageGeneratorTests
{
    private readonly QrCodeImageGenerator _generator = new();

    [Fact]
    public void PNGとして読める画像を返す()
    {
        var png = _generator.CreatePng("ABCDEFGHIJKLMNOPQRSTUVWXYZ234567");

        // PNG のシグネチャ
        Assert.True(png.Length > 0);
        Assert.Equal([0x89, 0x50, 0x4E, 0x47], png[..4]);
    }

    [Fact]
    public void 解像度を上げると画像も大きくなる()
    {
        var small = _generator.CreatePng("TOKEN", pixelsPerModule: 4);
        var large = _generator.CreatePng("TOKEN", pixelsPerModule: 16);

        Assert.True(large.Length > small.Length);
    }

    [Fact]
    public void 同じ内容からは同じ画像ができる()
    {
        // 再発行していないのにカードを作り直すたび絵柄が変わる、という状態にしない。
        Assert.Equal(_generator.CreatePng("TOKEN"), _generator.CreatePng("TOKEN"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void 空の内容は拒否される(string content)
    {
        Assert.Throws<ArgumentException>(() => _generator.CreatePng(content));
    }

    [Fact]
    public void 解像度は1以上でなければならない()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _generator.CreatePng("TOKEN", 0));
    }
}

public sealed class QrCardRendererTests
{
    [Fact]
    public void 名刺サイズのカード画像を作る()
    {
        var png = RunOnStaThread(() =>
        {
            var qr = new QrCodeImageGenerator().CreatePng("ABCDEFGHIJKLMNOPQRSTUVWXYZ234567");

            return new QrCardRenderer().RenderPng(new QrCardContent
            {
                StoreName = "相模原店",
                StaffName = "山田 太郎",
                NameKana = "ヤマダ タロウ",
                StaffNo = "E-0104",
                IssuedOn = new DateOnly(2026, 9, 6),
                QrPng = qr,
            });
        });

        Assert.Equal([0x89, 0x50, 0x4E, 0x47], png[..4]);

        // 91×55mm を 300dpi で。印刷しても粗くならないこと。
        var (width, height) = ReadPngSize(png);
        Assert.Equal(1075, width);
        Assert.Equal(650, height);
    }

    [Fact]
    public void 任意項目が無くても描画できる()
    {
        var png = RunOnStaThread(() =>
        {
            var qr = new QrCodeImageGenerator().CreatePng("TOKEN");

            return new QrCardRenderer().RenderPng(new QrCardContent
            {
                StoreName = "相模原店",
                StaffName = "山田 太郎",
                IssuedOn = new DateOnly(2026, 9, 6),
                QrPng = qr,
            });
        });

        Assert.True(png.Length > 0);
    }

    [Fact]
    public void 氏名が長くても描画が壊れない()
    {
        var png = RunOnStaThread(() =>
        {
            var qr = new QrCodeImageGenerator().CreatePng("TOKEN");

            return new QrCardRenderer().RenderPng(new QrCardContent
            {
                StoreName = new string('店', 40),
                StaffName = new string('名', 40),
                IssuedOn = new DateOnly(2026, 9, 6),
                QrPng = qr,
            });
        });

        // はみ出して QR に重なると読み取れなくなる。1行に収めて省略する実装。
        Assert.True(png.Length > 0);
    }

    /// <summary>
    /// WPF の描画は STA スレッドでしか動かない。xUnit は既定で MTA のため、
    /// 専用のスレッドを立てて実行する。
    /// </summary>
    private static byte[] RunOnStaThread(Func<byte[]> action)
    {
        byte[]? result = null;
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                result = action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            throw new InvalidOperationException("描画に失敗しました。", failure);
        }

        return result!;
    }

    private static (int Width, int Height) ReadPngSize(byte[] png)
    {
        // IHDR は 16 バイト目から幅・高さがビッグエンディアンで並ぶ
        var width = (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
        var height = (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23];

        return (width, height);
    }
}
