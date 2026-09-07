using System.Windows.Controls;
using QTimeRecord.App.ViewModels;

namespace QTimeRecord.App.Views;

public partial class StaffView : UserControl
{
    public StaffView() => InitializeComponent();

    /// <summary>
    /// 在籍状態の変更。選んだ時点で反映する。
    ///
    /// 「保存」を挟むと、変えたつもりで反映されていない状態が生まれる。
    /// 確認ダイアログと、取り消したときの表示の戻しは ViewModel が行う。
    /// </summary>
    private void OnStatusSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is StaffViewModel viewModel
            && StatusSelector.SelectedItem is StaffStatusOption option)
        {
            viewModel.ChangeStatusCommand.Execute(option);
        }
    }
}
