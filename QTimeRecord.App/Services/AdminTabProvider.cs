using Microsoft.Extensions.DependencyInjection;
using QTimeRecord.App.ViewModels;

namespace QTimeRecord.App.Services;

/// <summary>
/// 管理画面のタブを組み立てる。
///
/// シェルから切り出しているのは、シェル自身のふるまい（切り替え・ログアウト・
/// 打刻を受け付けないこと）を、3画面ぶんの依存を用意せずに確かめられるようにするため。
/// </summary>
public interface IAdminTabProvider
{
    IReadOnlyList<AdminTab> Create();
}

public sealed class AdminTabProvider(IServiceProvider provider) : IAdminTabProvider
{
    public IReadOnlyList<AdminTab> Create() =>
    [
        // 初期タブは勤務状況（→ plan.md 10-3-4）。管理者が最初に見たいのは今日の勤怠。
        new AdminTab("勤務状況", () => Start<AttendanceViewModel>(vm => vm.LoadAsync())),
        new AdminTab("スタッフ管理", () => Start<StaffViewModel>(vm => vm.LoadAsync())),
        new AdminTab("店舗設定", () => Start<StoreSettingsViewModel>(vm => vm.LoadAsync())),
    ];

    /// <summary>
    /// 中身を作り、読み込みを始める。
    ///
    /// 読み込みの完了を待たずに返す。待つとタブを押しても画面が固まったままになる。
    /// 読み込み中であることは各画面が自分で表示する。
    /// </summary>
    private TViewModel Start<TViewModel>(Func<TViewModel, Task> load)
        where TViewModel : class
    {
        var viewModel = provider.GetRequiredService<TViewModel>();

        _ = load(viewModel);

        return viewModel;
    }
}
