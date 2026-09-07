using System.Windows;

namespace QTimeRecord.App.Views.Dialogs;

/// <summary>
/// タッチ操作前提の共通ダイアログ。確認・エラー・通知に使い回す。
/// </summary>
public partial class TouchDialogWindow : Window
{
    public TouchDialogWindow(string title, string message, string okText, string? cancelText)
    {
        InitializeComponent();

        DialogTitle = title;
        TitleText.Text = title;
        MessageText.Text = message;
        OkButton.Content = okText;

        if (cancelText is null)
        {
            // 通知だけのダイアログ。選択肢を出すと「どちらか選ばないと閉じない」と誤解される。
            CancelButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            CancelButton.Content = cancelText;
        }
    }

    public string DialogTitle { get; }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
