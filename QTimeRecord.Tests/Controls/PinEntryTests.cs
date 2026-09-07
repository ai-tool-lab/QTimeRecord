using System.Windows.Input;
using QTimeRecord.App.Controls;

namespace QTimeRecord.Tests.Controls;

public sealed class PinEntryTests
{
    [Fact]
    public void 数字を末尾に足す()
    {
        Assert.Equal("1", PinEntry.Append(string.Empty, '1', 8));
        Assert.Equal("123", PinEntry.Append("12", '3', 8));
    }

    [Fact]
    public void 上限を超えて入力できない()
    {
        // 8桁を超えると、入れたつもりの桁がどこかで落ちる。上限で止める。
        Assert.Equal("12345678", PinEntry.Append("12345678", '9', 8));
    }

    [Theory]
    [InlineData('a')]
    [InlineData('-')]
    [InlineData(' ')]
    public void 数字以外は受け付けない(char input)
    {
        Assert.Equal("12", PinEntry.Append("12", input, 8));
    }

    [Fact]
    public void 一文字消去は末尾を削る()
    {
        Assert.Equal("12", PinEntry.Backspace("123"));
        Assert.Equal(string.Empty, PinEntry.Backspace("1"));
    }

    [Fact]
    public void 空文字の一文字消去は何も起きない()
    {
        Assert.Equal(string.Empty, PinEntry.Backspace(string.Empty));
        Assert.Equal(string.Empty, PinEntry.Backspace(null));
    }

    [Fact]
    public void 伏せ字は桁数だけを表す()
    {
        var masked = PinEntry.Mask("1234");

        // 入力そのものを画面に出さない。キオスク端末の画面は誰からも見える。
        Assert.Equal("●●●●", masked);
        Assert.DoesNotContain('1', masked);
    }

    [Fact]
    public void 桁数インジケータを出す()
    {
        Assert.Equal("0 / 8 桁", PinEntry.Indicator(string.Empty, 8));
        Assert.Equal("3 / 8 桁", PinEntry.Indicator("123", 8));
    }

    [Theory]
    [InlineData(Key.D0, '0')]
    [InlineData(Key.D7, '7')]
    [InlineData(Key.NumPad0, '0')]
    [InlineData(Key.NumPad9, '9')]
    public void 物理キーの数字を読み替える(Key key, char expected)
    {
        var resolved = PinEntry.Resolve(key);

        Assert.Equal(KeypadAction.Digit, resolved.Action);
        Assert.Equal(expected, resolved.Digit);
    }

    [Theory]
    [InlineData(Key.Back, KeypadAction.Backspace)]
    [InlineData(Key.Delete, KeypadAction.Clear)]
    [InlineData(Key.Enter, KeypadAction.Submit)]
    [InlineData(Key.A, KeypadAction.None)]
    [InlineData(Key.F1, KeypadAction.None)]
    public void 物理キーの操作を読み替える(Key key, KeypadAction expected)
    {
        Assert.Equal(expected, PinEntry.Resolve(key).Action);
    }
}
