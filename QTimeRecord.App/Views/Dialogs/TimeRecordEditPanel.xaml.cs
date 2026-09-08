using System.Windows;
using System.Windows.Controls;

namespace QTimeRecord.App.Views.Dialogs;

public partial class TimeRecordEditPanel : UserControl
{
    public TimeRecordEditPanel() => InitializeComponent();

    /// <summary>キャンセルが押された。ダイアログを閉じるのは Window の仕事。</summary>
    public event EventHandler? CancelRequested;

    private void OnCancel(object sender, RoutedEventArgs e)
        => CancelRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// 「翌日」の切り替え。
    ///
    /// 双方向の束縛にせず ViewModel のメソッドを通すのは、
    /// <b>手で切り替えたことを覚えさせる</b>ため。以後は時刻を打ち直しても
    /// 自動判定で上書きしない。
    /// </summary>
    private void OnNextDayClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.TimeRecordEditViewModel viewModel)
        {
            viewModel.ToggleNextDay(NextDayCheck.IsChecked == true);
        }
    }
}
