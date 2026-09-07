using System.Text;
using QTimeRecord.Core.Devices;

namespace QTimeRecord.Tests.Devices;

/// <summary>
/// 受信データの切り分け。機種差がそのまま出る箇所で、実機を触る前にここで潰しておく。
/// </summary>
public sealed class ScanBufferTests
{
    private static readonly DateTime Now = new(2026, 9, 6, 12, 0, 0);

    [Theory]
    [InlineData("TOKEN123\r")]
    [InlineData("TOKEN123\n")]
    [InlineData("TOKEN123\r\n")]
    public void 終端文字の種類を問わず1件として確定する(string input)
    {
        var buffer = new ScanBuffer();

        var results = Append(buffer, input);

        Assert.Equal(["TOKEN123"], results);
        Assert.False(buffer.HasPending);
    }

    [Fact]
    public void 分割して届いても復元できる()
    {
        var buffer = new ScanBuffer();
        var results = new List<string>();

        // 実機は1回の読み取りを複数回に分けて送ってくることがある
        foreach (var c in "TOKEN123\r")
        {
            results.AddRange(buffer.Append(Encoding.ASCII.GetBytes([c]), Now));
        }

        Assert.Equal(["TOKEN123"], results);
    }

    [Fact]
    public void 二件がまとめて届いても分けられる()
    {
        var buffer = new ScanBuffer();

        var results = Append(buffer, "FIRST\r\nSECOND\r\n");

        Assert.Equal(["FIRST", "SECOND"], results);
    }

    [Fact]
    public void 空行は無視される()
    {
        var buffer = new ScanBuffer();

        // CRLF 終端では Complete が 2 回呼ばれる。空を1件として流さないこと。
        var results = Append(buffer, "\r\n\r\nTOKEN\r\n\r\n");

        Assert.Equal(["TOKEN"], results);
    }

    [Fact]
    public void 制御文字は取り除かれる()
    {
        var buffer = new ScanBuffer();

        // STX(0x02) / ETX(0x03) を前後に付ける機種がある。
        // 残したままだとトークンが一致せず、登録済みのQRが「未登録」と判定される。
        var results = Append(buffer, "\u0002TOKEN123\u0003\r");

        Assert.Equal(["TOKEN123"], results);
    }

    [Fact]
    public void 空白だけの入力は1件として流さない()
    {
        var buffer = new ScanBuffer();

        Assert.Empty(Append(buffer, "\t \r\n"));
    }

    [Fact]
    public void 前後の空白は落とされる()
    {
        var buffer = new ScanBuffer();

        Assert.Equal(["TOKEN123"], Append(buffer, "  TOKEN123  \r"));
    }

    // ---- 終端を送らない機種 ----

    [Fact]
    public void 無受信が続けば終端なしでも確定する()
    {
        var buffer = new ScanBuffer(idleTimeout: TimeSpan.FromMilliseconds(200));

        Assert.Empty(Append(buffer, "TOKEN123"));
        Assert.True(buffer.HasPending);

        var flushed = buffer.FlushIfIdle(Now.AddMilliseconds(200));

        Assert.Equal("TOKEN123", flushed);
        Assert.False(buffer.HasPending);
    }

    [Fact]
    public void 待機時間内は確定しない()
    {
        var buffer = new ScanBuffer(idleTimeout: TimeSpan.FromMilliseconds(200));
        Append(buffer, "TOKEN");

        // まだ続きが届く可能性がある。早すぎる確定は文字列を途中で切ってしまう。
        Assert.Null(buffer.FlushIfIdle(Now.AddMilliseconds(199)));
        Assert.True(buffer.HasPending);
    }

    [Fact]
    public void 受信が無ければ確定するものも無い()
    {
        var buffer = new ScanBuffer();

        Assert.Null(buffer.FlushIfIdle(Now.AddSeconds(10)));
    }

    // ---- 異常系 ----

    [Fact]
    public void 長すぎる入力は捨てられる()
    {
        var buffer = new ScanBuffer(maxLength: 16);

        var results = Append(buffer, new string('A', 100) + "\r");

        // 途中で切れた文字列を打刻に使わせない。
        Assert.Empty(results);
        Assert.False(buffer.HasPending);
    }

    [Fact]
    public void 長すぎる入力のあと次の1件は正常に読める()
    {
        var buffer = new ScanBuffer(maxLength: 16);
        Append(buffer, new string('A', 100) + "\r");

        var results = Append(buffer, "TOKEN123\r");

        Assert.Equal(["TOKEN123"], results);
    }

    [Fact]
    public void リセットで受信途中の内容が消える()
    {
        var buffer = new ScanBuffer();
        Append(buffer, "HALF");

        buffer.Reset();

        Assert.False(buffer.HasPending);
        Assert.Equal(["TOKEN"], Append(buffer, "TOKEN\r"));
    }

    private static List<string> Append(ScanBuffer buffer, string text)
        => [.. buffer.Append(Encoding.ASCII.GetBytes(text), Now)];
}
