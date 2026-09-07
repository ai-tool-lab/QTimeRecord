using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using QTimeRecord.App.ViewModels;
using QTimeRecord.App.Views.Dialogs;
using QTimeRecord.Core.Domain;

namespace QTimeRecord.App.Services;

/// <summary>
/// スタッフの登録・編集ダイアログを開く。
///
/// ViewModel から Window を直接作らないためのもの。
/// 直接作ると、保存後に名簿が読み直されることをテストで確かめられなくなる。
/// </summary>
public interface IStaffEditor
{
    /// <summary>新規登録として開く。保存したら true。</summary>
    Task<bool> AddAsync();

    /// <summary>既存スタッフの編集として開く。保存したら true。</summary>
    Task<bool> EditAsync(Staff staff);
}

public sealed class StaffEditor(IServiceProvider provider) : IStaffEditor
{
    public Task<bool> AddAsync()
    {
        var viewModel = provider.GetRequiredService<StaffEditViewModel>();

        viewModel.OpenForAdd();

        return Task.FromResult(Show(viewModel));
    }

    public Task<bool> EditAsync(Staff staff)
    {
        var viewModel = provider.GetRequiredService<StaffEditViewModel>();

        viewModel.OpenForEdit(staff);

        return Task.FromResult(Show(viewModel));
    }

    private static bool Show(StaffEditViewModel viewModel)
    {
        var window = new StaffEditDialog
        {
            DataContext = viewModel,
            Owner = Application.Current?.MainWindow,
        };

        viewModel.Completed += (_, _) => window.DialogResult = true;

        return window.ShowDialog() == true;
    }
}
