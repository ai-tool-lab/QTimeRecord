using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QTimeRecord.App.Services;
using QTimeRecord.App.ViewModels;
using QTimeRecord.Core;
using QTimeRecord.Core.Infrastructure;

namespace QTimeRecord.App;

/// <summary>
/// DI の登録を1か所にまとめる。
///
/// App.xaml.cs に直接書かず切り出しているのは、
/// テストから同じ構成のコンテナを組み立てて「解決漏れ」を検出するため。
/// </summary>
public static class AppComposition
{
    public static IServiceCollection AddAppServices(
        this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection(AppSettings.SectionName).Get<AppSettings>()
            ?? new AppSettings();

        services.AddSingleton(settings);
        services.AddQTimeRecordCore(settings);

        AddUiServices(services);
        AddViewModels(services);
        AddWindows(services);

        return services;
    }

    private static void AddUiServices(IServiceCollection services)
    {
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IQrCardRenderer, QrCardRenderer>();
        services.AddSingleton<IAttendanceEditor, AttendanceEditor>();
        services.AddSingleton<IAdminTabProvider, AdminTabProvider>();
        services.AddSingleton<IStaffEditor, StaffEditor>();
        services.AddSingleton<IStaffCardService, StaffCardService>();
        services.AddSingleton<IFileDialogService, FileDialogService>();
        services.AddSingleton<IUiTicker, UiTicker>();

        // 無操作タイマーは画面ごとに1つ持ち、画面を離れるときに破棄する。
        // 共有すると、前の画面のカウントダウンが次の画面を巻き戻す。
        services.AddTransient<IdleTimeoutService>();
        services.AddSingleton<ScannerHost>();
        services.AddSingleton<ScreenFlow>();
        services.AddSingleton<GlobalExceptionHandler>();
    }

    private static void AddViewModels(IServiceCollection services)
    {
        // シェルはウィンドウと1対1。NavigationService の通知を購読するため、
        // 複数生成されると購読が積み重なる。単一で登録する。
        services.AddSingleton<ShellViewModel>();

        // 画面の ViewModel は遷移のたびに作り直す。
        // 前の状態（エラー表示や入力途中の値）を持ち越さないため。
        services.AddTransient<SetupViewModel>();
        services.AddTransient<IdleViewModel>();
        services.AddTransient<PunchSelectViewModel>();
        services.AddTransient<PunchResultViewModel>();
        services.AddTransient<AdminLoginViewModel>();
        services.AddTransient<AdminShellViewModel>();
        services.AddTransient<AttendanceViewModel>();
        services.AddTransient<TimeRecordEditViewModel>();
        services.AddTransient<StaffViewModel>();
        services.AddTransient<StaffEditViewModel>();
        services.AddTransient<StoreSettingsViewModel>();
    }

    private static void AddWindows(IServiceCollection services)
    {
        // ウィンドウは STA スレッドでしか生成できない。テストでは解決しないこと。
        services.AddSingleton<MainWindow>();
    }
}
