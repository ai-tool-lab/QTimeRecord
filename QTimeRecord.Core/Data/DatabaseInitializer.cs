using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace QTimeRecord.Core.Data;

public interface IDatabaseInitializer
{
    /// <summary>未適用のマイグレーションを適用する。</summary>
    Task MigrateAsync(CancellationToken ct = default);
}

/// <summary>
/// 起動時に DB を最新のスキーマへ揃える。
///
/// <c>EnsureCreated</c> は使わない。スキーマの変更を追跡できず、
/// 次のバージョンで列を足したときに移行できなくなる。
/// </summary>
public sealed class DatabaseInitializer(
    IDbContextFactory<QTimeRecordDbContext> factory,
    ILogger<DatabaseInitializer> logger) : IDatabaseInitializer
{
    public async Task MigrateAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();

        if (pending.Count == 0)
        {
            logger.LogInformation("データベースは最新です。");
            return;
        }

        logger.LogInformation("マイグレーションを適用します: {Migrations}", string.Join(", ", pending));

        await db.Database.MigrateAsync(ct);

        logger.LogInformation("マイグレーションを適用しました。");
    }
}
