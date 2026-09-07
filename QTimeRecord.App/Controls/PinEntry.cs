using System.Windows.Input;

namespace QTimeRecord.App.Controls;

/// <summary>テンキーで押されたものの種類。</summary>
public enum KeypadAction
{
    None,
    Digit,
    Backspace,
    Clear,
    Submit,
}

/// <param name="Action">操作の種類。</param>
/// <param name="Digit">数字キーのときの文字。それ以外は <c>'\0'</c>。</param>
public readonly record struct KeypadKey(KeypadAction Action, char Digit);

/// <summary>
/// PIN 入力の文字列操作。
///
/// WPF に依存させない。桁数の上限や伏せ字の扱いは画面のふるまいそのもので、
/// コントロールの中に埋めるとテストできなくなる。
/// </summary>
public static class PinEntry
{
    /// <summary>伏せ字に使う文字。</summary>
    public const char MaskChar = '●';

    public static string Append(string? current, char digit, int maxLength)
    {
        var value = current ?? string.Empty;

        if (!char.IsAsciiDigit(digit) || value.Length >= maxLength)
        {
            return value;
        }

        return value + digit;
    }

    public static string Backspace(string? current)
    {
        var value = current ?? string.Empty;

        return value.Length == 0 ? value : value[..^1];
    }

    /// <summary>
    /// 画面に出す伏せ字。
    ///
    /// 入力そのものは返さない。キオスク端末の画面は誰からも見える。
    /// </summary>
    public static string Mask(string? current) => new(MaskChar, (current ?? string.Empty).Length);

    /// <summary>「3 / 8 桁」。伏せ字だけだと何桁入れたか分からない。</summary>
    public static string Indicator(string? current, int maxLength)
        => $"{(current ?? string.Empty).Length} / {maxLength} 桁";

    /// <summary>
    /// 物理キーボードのキーをテンキーの操作に読み替える。
    ///
    /// タッチ端末でも USB キーボードが挿さっていることは多く、
    /// 管理者はそちらのほうが速い。
    /// </summary>
    public static KeypadKey Resolve(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => new KeypadKey(KeypadAction.Digit, (char)('0' + (key - Key.D0))),
        >= Key.NumPad0 and <= Key.NumPad9
            => new KeypadKey(KeypadAction.Digit, (char)('0' + (key - Key.NumPad0))),
        Key.Back => new KeypadKey(KeypadAction.Backspace, '\0'),
        Key.Delete => new KeypadKey(KeypadAction.Clear, '\0'),
        Key.Enter => new KeypadKey(KeypadAction.Submit, '\0'),
        _ => new KeypadKey(KeypadAction.None, '\0'),
    };
}
