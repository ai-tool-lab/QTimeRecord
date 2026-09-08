using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using QTimeRecord.App.ViewModels;
using QTimeRecord.App.Views.Dialogs;
using QTimeRecord.Core.Data.Repositories;
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

public sealed class AttendanceEditor(IServiceProvider provider, IStoreRepository stores)
    : IAttendanceEditor
{
    public async Task<bool> AddAsync(IReadOnlyList<StaffOption> staff, DateOnly workDate)
    {
        var viewModel = provider.GetRequiredService<TimeRecordEditViewModel>();

        viewModel.OpenForAdd(staff, workDate, await BusinessDayStartAsync());

        return Show(viewModel);
    }

    public async Task<bool> EditAsync(IReadOnlyList<StaffOption> staff, AttendanceRow row)
    {
        var viewModel = provider.GetRequiredService<TimeRecordEditViewModel>();

        viewModel.OpenForRow(staff, row, await BusinessDayStartAsync());

        return Show(viewModel);
    }

    /// <summary>
    /// 店舗の1日の開始時刻。ダイアログが「翌日の打刻か」を判断するのに使う。
    ///
    /// 開くたびに読む。設定を変えた直後に古い値で判断すると、
    /// 深夜の打刻が1日ずれて保存される。
    /// </summary>
    private async Task<TimeOnly> BusinessDayStartAsync()
        => (await stores.GetAsync())?.BusinessDayStart ?? new TimeOnly(0, 0);

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
