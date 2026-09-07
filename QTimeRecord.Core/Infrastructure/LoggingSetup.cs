using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;

namespace QTimeRecord.Core.Infrastructure;

/// <summary>
/// ログ出力の設定。
///
/// 営業時間中に動き続けるアプリなので、放っておくとログが際限なく溜まる。
/// 日次でファイルを分け、30世代を超えたものは自動で消す。
/// </summary>
public static class LoggingSetup
{
    /// <summary>保持世代数。1日1ファイルなので約1か月分。</summary>
    public const int RetainedFileCount = 30;

    /// <summary>1ファイルの上限。超えたら連番を付けて分ける。</summary>
    public const long FileSizeLimitBytes = 10 * 1024 * 1024;

    private const string OutputTemplate =
        "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}";

    public static Serilog.Core.Logger CreateLogger(AppPaths paths, AppSettings settings)
    {
        paths.EnsureCreated();

        return new LoggerConfiguration()
            .MinimumLevel.Is(ParseLevel(settings.LogLevel))

            // EF Core は既定で実行した SQL をすべて Information で出す。
            // 打刻のたびに数行ずつ増え、本来残したい記録が SQL に埋もれる。
            // 問い合わせの中身が要るのは調査時だけなので、既定では警告以上に絞る。
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.File(
                Path.Combine(paths.LogDirectory, "qtimerecord-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: RetainedFileCount,
                fileSizeLimitBytes: FileSizeLimitBytes,
                rollOnFileSizeLimit: true,
                outputTemplate: OutputTemplate,
                shared: true)
            .CreateLogger();
    }

    public static IServiceCollection AddQTimeRecordLogging(
        this IServiceCollection services, AppPaths paths, AppSettings settings)
    {
        var logger = CreateLogger(paths, settings);

        services.AddLogging(builder =>
        {
            // ホスト既定のコンソール出力は残さない。
            // 配布形態が WinExe でコンソールが無く、書き込みが無駄になるため。
            builder.ClearProviders();
            builder.AddSerilog(logger, dispose: true);
        });

        return services;
    }

    /// <summary>
    /// 設定文字列からレベルへ。読めない値なら Information に倒す。
    /// 設定の打ち間違いでログが一切出なくなる、という事態を避ける。
    /// </summary>
    private static LogEventLevel ParseLevel(string? value)
        => Enum.TryParse<LogEventLevel>(value, ignoreCase: true, out var level)
            ? level
            : LogEventLevel.Information;
}
