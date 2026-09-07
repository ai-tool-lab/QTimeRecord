using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace QTimeRecord.App.Views.Dialogs;

/// <summary>打刻の手動登録・修正ダイアログ。中身は TimeRecordEditPanel が持つ。</summary>
public partial class TimeRecordEditDialog : Window
{
    public TimeRecordEditDialog()
    {
        InitializeComponent();

        // 物理キーボードのテンキーでも入れられるようにする。管理者はそちらのほうが速い。
        Loaded += (_, _) => Panel.Keypad.Focus();

        Panel.CancelRequested += (_, _) => DialogResult = false;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            e.Handled = true;
            return;
        }

        // メモを打っている間は横取りしない。
        if (e.OriginalSource is TextBox)
        {
            base.OnPreviewKeyDown(e);
            return;
        }

        if (Panel.Keypad.HandleKey(e.Key))
        {
            e.Handled = true;
            return;
        }

        base.OnPreviewKeyDown(e);
    }
}
