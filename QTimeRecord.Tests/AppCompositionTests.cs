using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QTimeRecord.App;
using QTimeRecord.Core.Infrastructure;

namespace QTimeRecord.Tests;

/// <summary>
/// DI の解決漏れを検出する。
///
/// 登録忘れは起動して初めて例外になり、画面を追加した本人以外が踏むことが多い。
/// ここで落としておけば、実行せずにビルド時点で気づける。
/// </summary>
public sealed class AppCompositionTests
{
    private static ServiceCollection BuildServices()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["QTimeRecord:DatabasePath"] = @"C:\dummy\qtimerecord.db",
                ["QTimeRecord:LogDirectory"] = @"C:\dummy\logs",
                ["QTimeRecord:LogLevel"] = "Debug",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddAppServices(configuration);

        return services;
    }

    [Fact]
    public void 登録済みのViewModelがすべて解決できる()
    {
        var services = BuildServices();

        // ValidateOnBuild は「生成できるか」を実体化せずに検証する。
        // Window は STA が要るため、この方式でしか確認できない。
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });

        var viewModelTypes = services
            .Select(descriptor => descriptor.ServiceType)
            .Where(type => type.Name.EndsWith("ViewModel", StringComparison.Ordinal))
            .Distinct()
            .ToList();

        Assert.NotEmpty(viewModelTypes);

        foreach (var type in viewModelTypes)
        {
            Assert.NotNull(provider.GetRequiredService(type));
        }
    }

    [Fact]
    public void appsettingsの値がAppSettingsに反映される()
    {
        using var provider = BuildServices().BuildServiceProvider();

        var settings = provider.GetRequiredService<AppSettings>();

        Assert.Equal(@"C:\dummy\qtimerecord.db", settings.DatabasePath);
        Assert.Equal("Debug", settings.LogLevel);
    }

    [Fact]
    public void 環境変数を含むパスが展開される()
    {
        var settings = new AppSettings
        {
            DatabasePath = @"%LOCALAPPDATA%\QTimeRecord\qtimerecord.db",
        };

        var resolved = settings.ResolvedDatabasePath;

        Assert.DoesNotContain("%", resolved, StringComparison.Ordinal);
        Assert.EndsWith(@"\QTimeRecord\qtimerecord.db", resolved, StringComparison.Ordinal);
    }
}
