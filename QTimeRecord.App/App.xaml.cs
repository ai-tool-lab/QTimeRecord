using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QTimeRecord.App.Services;
using QTimeRecord.Core.Data;

namespace QTimeRecord.App;

/// <summary>
/// アプリケーションの起動と終了。ここが唯一の合成点（composition root）。
/// </summary>
public partial class App : Application
{
    private IHost? _host;
    private ILogger<App>? _logger;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ApplyJapaneseCulture();

        // 実行ファイルの場所を基準にする。
        // 既定はカレントディレクトリで、ショートカット経由の起動だと
        // appsettings.json を見失うことがあるため明示する。
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            ContentRootPath = AppContext.BaseDirectory,
            Args = e.Args,
        });

        builder.Services.AddAppServices(builder.Configuration);

        _host = builder.Build();
        _host.Start();

        // 例外ハンドラは、失敗しうる処理より先に付ける。
        _host.Services.GetRequiredService<GlobalExceptionHandler>().Attach(this);

        _logger = _host.Services.GetRequiredService<ILogger<App>>();
        _logger.LogInformation("QTimeRecord を起動しました。");

        try
        {
            // 画面を出す前にスキーマを揃える。
            // 途中で失敗したまま画面を出すと、打刻の瞬間に初めて壊れていることが分かる。
            _host.Services.GetRequiredService<IDatabaseInitializer>()
                .MigrateAsync()
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception ex)
        {
            // DB を用意できないなら打刻が一切できない。黙って画面を出さない。
            _logger.LogCritical(ex, "データベースを初期化できませんでした。");

            _host.Services.GetRequiredService<IDialogService>().ShowError(
                "起動できません",
                "データベースを初期化できませんでした。"
                + $"{Environment.NewLine}ログを確認し、管理者へ連絡してください。");

            Shutdown(1);
            return;
        }

        var window = _host.Services.GetRequiredService<MainWindow>();
        MainWindow = window;
        window.Show();

        // 画面を出してから最初の遷移を決める。
        // 先に DB を読むと、その間ウィンドウが出ずに固まって見える。
        _ = _host.Services.GetRequiredService<ScreenFlow>().StartAsync();
    }

    /// <summary>
    /// 表示を日本語の書式に固定する。
    ///
    /// <b>WPF の束縛は <c>FrameworkElement.Language</c> を見ており、既定は en-US。</b>
    /// スレッドのカルチャを設定しても効かず、日本語 Windows でも曜日が "Sunday" と出る。
    /// 打刻機の画面としては誤りなので、要素側の既定ごと差し替える。
    /// </summary>
    private static void ApplyJapaneseCulture()
    {
        var culture = new CultureInfo("ja-JP");

        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;

        FrameworkElement.LanguageProperty.OverrideMetadata(
            typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(culture.IetfLanguageTag)));
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _logger?.LogInformation("QTimeRecord を終了します。（終了コード {ExitCode}）", e.ApplicationExitCode);

        if (_host is not null)
        {
            // Serilog は dispose 時に書き出しを完了させる。ホストの破棄で連鎖する。
            _host.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            _host.Dispose();
            _host = null;
        }

        base.OnExit(e);
    }
}
