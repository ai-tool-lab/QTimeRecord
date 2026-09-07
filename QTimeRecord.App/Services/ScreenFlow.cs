using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QTimeRecord.App.ViewModels;
using QTimeRecord.Core.Data.Repositories;
using QTimeRecord.Core.Domain;
using QTimeRecord.Core.Services;

namespace QTimeRecord.App.Services;

/// <summary>
/// 画面の並び順を決める。
///
/// <code>
/// 初回セットアップ ─▶ 待機 ─(有効QR)─▶ 打刻種別の選択 ─(打刻)─▶ 完了 ─▶ 待機
///                       ▲  │                 │
///                       │  └(管理画面)─▶ ログイン ─(認証)─▶ 管理画面 ─(ログアウト)─┐
///                       └──(キャンセル・時間切れ)──────────────────────────────────┘
/// </code>
///
/// <b>遷移を知っているのはこのクラスだけにする。</b>
/// ViewModel が次の画面を直接作ると、画面どうしが互いを参照し合って
/// 「どこから来たのか」を各画面が持たなければならなくなる。
/// ViewModel はイベントで「終わった」ことだけを伝える。
/// </summary>
public sealed class ScreenFlow(
    IStoreSetupService setup,
    IStoreRepository stores,
    INavigationService navigation,
    IServiceProvider provider,
    ScannerHost scannerHost,
    IUiTicker ticker,
    ILogger<ScreenFlow> logger)
{
    public async Task StartAsync(CancellationToken ct = default)
    {
        ticker.Start();

        if (await setup.IsInitializedAsync(ct))
        {
            await ShowIdleAsync(ct);
            return;
        }

        // 未セットアップの状態で待機画面を出すと、店舗名もお知らせも空のまま
        // 「QRをかざしてください」とだけ表示され、何をすべきか分からない画面になる。
        logger.LogInformation("未セットアップのため、初回セットアップ画面を表示します。");
        ShowSetup();
    }

    private void ShowSetup()
    {
        var viewModel = provider.GetRequiredService<SetupViewModel>();

        viewModel.Completed += async (_, _) => await ShowIdleAsync();

        navigation.NavigateTo(viewModel);
    }

    private async Task ShowIdleAsync(CancellationToken ct = default)
    {
        var store = await stores.GetAsync(ct);

        if (store is null)
        {
            logger.LogError("店舗が見つかりません。セットアップからやり直してください。");
            ShowSetup();
            return;
        }

        // リーダーは画面ごとではなくアプリ全体で1つだけ動かす。
        await scannerHost.StartAsync(store.Id, ct);

        var viewModel = provider.GetRequiredService<IdleViewModel>();
        await viewModel.LoadAsync(ct);

        viewModel.StaffIdentified += async (_, staff) => await ShowPunchSelectAsync(staff);
        viewModel.AdminRequested += async (_, _) => await ShowAdminLoginAsync();

        navigation.NavigateTo(viewModel);
    }

    private async Task ShowAdminLoginAsync()
    {
        var viewModel = provider.GetRequiredService<AdminLoginViewModel>();

        viewModel.Authenticated += (_, _) => ShowAdminShell();
        viewModel.Cancelled += async (_, _) => await ShowIdleAsync();

        navigation.NavigateTo(viewModel);
        await viewModel.LoadAsync();
    }

    private void ShowAdminShell()
    {
        var viewModel = provider.GetRequiredService<AdminShellViewModel>();

        viewModel.LoggedOut += async (_, _) => await ShowIdleAsync();

        navigation.NavigateTo(viewModel);
    }

    private async Task ShowPunchSelectAsync(Staff staff)
    {
        var viewModel = provider.GetRequiredService<PunchSelectViewModel>();

        viewModel.Punched += (_, outcome) =>
        {
            if (outcome.Record is not null)
            {
                ShowResult(staff, outcome.Record);
            }
        };

        viewModel.Cancelled += async (_, _) => await ShowIdleAsync();

        // 遷移してから読み込む。先に DB を読むと、その間だけ待機画面が固まって見える。
        navigation.NavigateTo(viewModel);
        await viewModel.LoadAsync(staff.Id);
    }

    private void ShowResult(Staff staff, TimeRecord record)
    {
        var viewModel = provider.GetRequiredService<PunchResultViewModel>();

        viewModel.Finished += async (_, _) => await ShowIdleAsync();

        navigation.NavigateTo(viewModel);
        viewModel.Show(staff, record);
    }
}
