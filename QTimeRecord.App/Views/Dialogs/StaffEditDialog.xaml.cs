using System.Windows;
using System.Windows.Input;

namespace QTimeRecord.App.Views.Dialogs;

/// <summary>スタッフの登録・編集ダイアログ。中身は StaffEditPanel が持つ。</summary>
public partial class StaffEditDialog : Window
{
    public StaffEditDialog()
    {
        InitializeComponent();

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

        base.OnPreviewKeyDown(e);
    }
}
