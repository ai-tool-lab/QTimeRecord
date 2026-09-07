using System.Windows;
using System.Windows.Controls;

namespace QTimeRecord.App.Views.Dialogs;

public partial class StaffEditPanel : UserControl
{
    public StaffEditPanel() => InitializeComponent();

    /// <summary>キャンセルが押された。ダイアログを閉じるのは Window の仕事。</summary>
    public event EventHandler? CancelRequested;

    private void OnCancel(object sender, RoutedEventArgs e)
        => CancelRequested?.Invoke(this, EventArgs.Empty);
}
