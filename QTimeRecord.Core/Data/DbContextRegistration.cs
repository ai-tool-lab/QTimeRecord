using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using QTimeRecord.Core.Infrastructure;

namespace QTimeRecord.Core.Data;

public static class DbContextRegistration
{
    /// <summary>
    /// DB 関連を DI へ登録する。
    ///
    /// <b>DbContext そのものではなくファクトリを登録する。</b>
    /// デスクトップアプリには Web のようなリクエスト境界が無く、DbContext を長生きさせると
    /// 追跡中のエンティティが溜まり、他画面の更新が反映されない状態になる。
    /// 操作ごとに短命なコンテキストを作る。
    /// </summary>
    public static IServiceCollection AddQTimeRecordDatabase(
        this IServiceCollection services, AppSettings settings)
    {
        services.AddSingleton<SqlitePragmaInterceptor>();

        services.AddDbContextFactory<QTimeRecordDbContext>((provider, options) =>
        {
            options.UseSqlite(BuildConnectionString(settings.ResolvedDatabasePath));
            options.AddInterceptors(provider.GetRequiredService<SqlitePragmaInterceptor>());
        });

        return services;
    }

    public static string BuildConnectionString(string databasePath)
    {
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return $"Data Source={databasePath}";
    }
}

/// <summary>
/// <c>dotnet ef migrations add</c> がコンテキストを組み立てるために使う。
/// アプリの起動処理を通さずに実行されるため、ここで最低限の構成を与える。
/// 実行時には使われない。
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<QTimeRecordDbContext>
{
    public QTimeRecordDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<QTimeRecordDbContext>()
            .UseSqlite("Data Source=qtimerecord-design.db")
            .Options;

        return new QTimeRecordDbContext(options);
    }
}
