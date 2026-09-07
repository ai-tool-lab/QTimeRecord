using System.Windows.Controls;
using System.Windows.Input;
using QTimeRecord.App.Controls;
using QTimeRecord.App.ViewModels;

namespace QTimeRecord.App.Views;

public partial class AdminLoginView : UserControl
{
    public AdminLoginView()
    {
        InitializeComponent();

        // キーボード入力を受けるには誰かがフォーカスを持っている必要がある。
        // タッチだけで使う端末では誰も触らないため、表示時にこの画面へ移す。
        Loaded += (_, _) => Focus();

        Keypad.Submitted += (_, _) => Submit();
    }

    /// <summary>
    /// 物理キーボードの入力をテンキーへ流す。
    ///
    /// タッチ端末でも USB キーボードが挿さっていることは多く、管理者はそちらのほうが速い。
    /// ロック中は受け付けない（テンキー自体が無効になっている）。
    /// </summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            (DataContext as AdminLoginViewModel)?.CancelCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (Keypad.IsEnabled && Keypad.HandleKey(e.Key))
        {
            e.Handled = true;
        }
    }

    private void Submit()
    {
        if (DataContext is AdminLoginViewModel viewModel && viewModel.SubmitCommand.CanExecute(null))
        {
            viewModel.SubmitCommand.Execute(null);
        }
    }
}
