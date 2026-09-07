using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using QTimeRecord.App.ViewModels;
using QTimeRecord.App.Views.Dialogs;
using QTimeRecord.Core.Services;

namespace QTimeRecord.App.Services;

/// <summary>
/// 打刻の編集ダイアログを開く。
///
/// ViewModel から Window を直接作らないためのもの。
/// 直接作ると、一覧が保存後に読み込み直されることをテストで確かめられなくなる。
/// </summary>
public interface IAttendanceEditor
{
    /// <summary>手動登録として開く。保存したら true。</summary>
    Task<bool> AddAsync(IReadOnlyList<StaffOption> staff, DateOnly workDate);

    /// <summary>一覧の行から開く。保存・削除したら true。</summary>
    Task<bool> EditAsync(IReadOnlyList<StaffOption> staff, AttendanceRow row);
}

public sealed class AttendanceEditor(IServiceProvider provider) : IAttendanceEditor
{
    public Task<bool> AddAsync(IReadOnlyList<StaffOption> staff, DateOnly workDate)
    {
        var viewModel = provider.GetRequiredService<TimeRecordEditViewModel>();

        viewModel.OpenForAdd(staff, workDate);

        return Task.FromResult(Show(viewModel));
    }

    public Task<bool> EditAsync(IReadOnlyList<StaffOption> staff, AttendanceRow row)
    {
        var viewModel = provider.GetRequiredService<TimeRecordEditViewModel>();

        viewModel.OpenForRow(staff, row);

        return Task.FromResult(Show(viewModel));
    }

    private static bool Show(TimeRecordEditViewModel viewModel)
    {
        var window = new TimeRecordEditDialog
        {
            DataContext = viewModel,
            Owner = Application.Current?.MainWindow,
        };

        viewModel.Completed += (_, _) => window.DialogResult = true;

        return window.ShowDialog() == true;
    }
}
