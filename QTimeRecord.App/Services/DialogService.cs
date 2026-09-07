using System.Windows;
using QTimeRecord.App.Views.Dialogs;

namespace QTimeRecord.App.Services;

public interface IDialogService
{
    /// <summary>はい／いいえの確認。破棄的な操作の前に必ず通す。</summary>
    bool Confirm(string title, string message, string okText = "OK", string cancelText = "キャンセル");

    void ShowInfo(string title, string message);

    void ShowError(string title, string message);
}

/// <summary>
/// ダイアログ表示。
///
/// ViewModel から <c>MessageBox</c> を直接呼ばないためのもの。
/// 直接呼ぶとテストできなくなるうえ、ボタンサイズがタッチ操作に耐えない。
/// </summary>
public sealed class DialogService : IDialogService
{
    public bool Confirm(string title, string message, string okText = "OK", string cancelText = "キャンセル")
        => Show(title, message, okText, cancelText) == true;

    public void ShowInfo(string title, string message)
        => Show(title, message, "閉じる", cancelText: null);

    public void ShowError(string title, string message)
        => Show(title, message, "閉じる", cancelText: null);

    private static bool? Show(string title, string message, string okText, string? cancelText)
    {
        var dialog = new TouchDialogWindow(title, message, okText, cancelText)
        {
            Owner = Application.Current?.MainWindow,
        };

        return dialog.ShowDialog();
    }
}
