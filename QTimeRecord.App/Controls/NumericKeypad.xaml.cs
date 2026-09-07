using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace QTimeRecord.App.Controls;

/// <summary>
/// タッチ用のテンキー。
///
/// 入力そのものは <see cref="Value"/> に持ち、画面側が伏せ字にして表示する。
/// 文字列の操作は <see cref="PinEntry"/> に置き、ここは WPF の受け口だけにする。
/// </summary>
public partial class NumericKeypad : UserControl
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value),
        typeof(string),
        typeof(NumericKeypad),
        new FrameworkPropertyMetadata(
            string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty MaxLengthProperty = DependencyProperty.Register(
        nameof(MaxLength), typeof(int), typeof(NumericKeypad), new PropertyMetadata(8));

    public NumericKeypad() => InitializeComponent();

    /// <summary>Enter が押された。画面側が確定処理を行う。</summary>
    public event EventHandler? Submitted;

    public string Value
    {
        get => (string)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public int MaxLength
    {
        get => (int)GetValue(MaxLengthProperty);
        set => SetValue(MaxLengthProperty, value);
    }

    /// <summary>
    /// 物理キーボードの入力を受け取る。処理したら true。
    /// タッチ端末でもキーボードが挿さっていることは多く、管理者はそちらのほうが速い。
    /// </summary>
    public bool HandleKey(Key key)
    {
        var resolved = PinEntry.Resolve(key);

        switch (resolved.Action)
        {
            case KeypadAction.Digit:
                Value = PinEntry.Append(Value, resolved.Digit, MaxLength);
                return true;

            case KeypadAction.Backspace:
                Value = PinEntry.Backspace(Value);
                return true;

            case KeypadAction.Clear:
                Value = string.Empty;
                return true;

            case KeypadAction.Submit:
                Submitted?.Invoke(this, EventArgs.Empty);
                return true;

            default:
                return false;
        }
    }

    private void OnDigitClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && tag.Length == 1)
        {
            Value = PinEntry.Append(Value, tag[0], MaxLength);
        }
    }

    private void OnBackspaceClick(object sender, RoutedEventArgs e)
        => Value = PinEntry.Backspace(Value);

    private void OnClearClick(object sender, RoutedEventArgs e) => Value = string.Empty;
}
