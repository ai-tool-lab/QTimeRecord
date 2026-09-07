using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QTimeRecord.Core.Infrastructure;

namespace QTimeRecord.Tests.Infrastructure;

public sealed class LogSafeTests
{
    [Fact]
    public void トークンは指紋に変換される()
    {
        const string token = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

        var fingerprint = LogSafe.TokenFingerprint(token);

        // 8文字の16進。ログが漏れても、この値からQRは復元できない。
        Assert.Equal(8, fingerprint.Length);
        Assert.Matches("^[0-9a-f]{8}$", fingerprint);
        Assert.DoesNotContain(token, fingerprint, StringComparison.Ordinal);
    }

    [Fact]
    public void 同じトークンは同じ指紋になる()
    {
        // 「同じQRが繰り返し読まれている」ことを追えるようにするため。
        Assert.Equal(LogSafe.TokenFingerprint("TOKEN-A"), LogSafe.TokenFingerprint("TOKEN-A"));
        Assert.NotEqual(LogSafe.TokenFingerprint("TOKEN-A"), LogSafe.TokenFingerprint("TOKEN-B"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void 空のトークンでも落ちない(string? token)
    {
        Assert.Equal("(空)", LogSafe.TokenFingerprint(token));
    }
}

public sealed class LoggingSetupTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "qtimerecord-logs", Guid.NewGuid().ToString("N"));

    [Fact]
    public void ログファイルが作られて書き込まれる()
    {
        var services = new ServiceCollection();
        services.AddQTimeRecordLogging(CreatePaths(), CreateSettings("Information"));

        using (var provider = services.BuildServiceProvider())
        {
            provider.GetRequiredService<ILogger<LoggingSetupTests>>()
                .LogInformation("テスト用のメッセージ");
        }

        // provider の破棄で Serilog が書き出しを完了する
        var files = Directory.GetFiles(_directory, "qtimerecord-*.log");

        Assert.Single(files);
        Assert.Contains("テスト用のメッセージ", File.ReadAllText(files[0]), StringComparison.Ordinal);
    }

    [Fact]
    public void 設定したレベル未満は出力されない()
    {
        var services = new ServiceCollection();
        services.AddQTimeRecordLogging(CreatePaths(), CreateSettings("Warning"));

        using (var provider = services.BuildServiceProvider())
        {
            var logger = provider.GetRequiredService<ILogger<LoggingSetupTests>>();
            logger.LogInformation("出ないはずのメッセージ");
            logger.LogWarning("出るはずのメッセージ");
        }

        var content = File.ReadAllText(Directory.GetFiles(_directory, "qtimerecord-*.log")[0]);

        Assert.DoesNotContain("出ないはずの", content, StringComparison.Ordinal);
        Assert.Contains("出るはずの", content, StringComparison.Ordinal);
    }

    [Fact]
    public void EFCoreのSQLログは既定で抑制される()
    {
        var services = new ServiceCollection();
        services.AddQTimeRecordLogging(CreatePaths(), CreateSettings("Information"));

        using (var provider = services.BuildServiceProvider())
        {
            var factory = provider.GetRequiredService<ILoggerFactory>();

            // 打刻のたびに SQL が数行ずつ増えると、残したい記録が埋もれる。
            factory.CreateLogger("Microsoft.EntityFrameworkCore.Database.Command")
                .LogInformation("SELECT * FROM time_records");

            factory.CreateLogger("QTimeRecord.Core.Services.PunchService")
                .LogInformation("打刻を保存しました");
        }

        var content = File.ReadAllText(Directory.GetFiles(_directory, "qtimerecord-*.log")[0]);

        Assert.DoesNotContain("SELECT * FROM", content, StringComparison.Ordinal);
        Assert.Contains("打刻を保存しました", content, StringComparison.Ordinal);
    }

    [Fact]
    public void 読めないレベル指定でも出力は止まらない()
    {
        // 設定の打ち間違いでログが一切出なくなると、障害調査の手段を失う。
        var services = new ServiceCollection();
        services.AddQTimeRecordLogging(CreatePaths(), CreateSettings("でたらめ"));

        using (var provider = services.BuildServiceProvider())
        {
            provider.GetRequiredService<ILogger<LoggingSetupTests>>().LogInformation("既定レベルで出る");
        }

        var content = File.ReadAllText(Directory.GetFiles(_directory, "qtimerecord-*.log")[0]);

        Assert.Contains("既定レベルで出る", content, StringComparison.Ordinal);
    }

    private AppPaths CreatePaths() => new(CreateSettings("Information"));

    private AppSettings CreateSettings(string level) => new()
    {
        DatabasePath = Path.Combine(_directory, "qtimerecord.db"),
        LogDirectory = _directory,
        LogLevel = level,
    };

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // 後始末の失敗でテストを落とさない
        }
    }
}
