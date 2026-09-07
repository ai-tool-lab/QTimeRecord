using Microsoft.EntityFrameworkCore;
using QTimeRecord.Core.Domain;

namespace QTimeRecord.Core.Data.Repositories;

public interface IStoreRepository
{
    /// <summary>店舗を1件取得する。1端末1店舗のため、あれば常にこの1件。</summary>
    Task<Store?> GetAsync(CancellationToken ct = default);

    Task<bool> ExistsAsync(CancellationToken ct = default);

    Task AddAsync(Store store, CancellationToken ct = default);

    Task UpdateAsync(Store store, CancellationToken ct = default);

    Task<IReadOnlyList<Announcement>> GetAnnouncementsAsync(Guid storeId, CancellationToken ct = default);

    /// <summary>お知らせを丸ごと置き換える。表示順は渡された並びで振り直す。</summary>
    Task ReplaceAnnouncementsAsync(
        Guid storeId, IReadOnlyList<Announcement> announcements, CancellationToken ct = default);
}

public sealed class StoreRepository(IDbContextFactory<QTimeRecordDbContext> factory) : IStoreRepository
{
    /// <summary>お知らせの上限。画面に収まる件数として決めた（→ plan.md Q4）。</summary>
    public const int MaxAnnouncements = 5;

    public async Task<Store?> GetAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        return await db.Stores.AsNoTracking().FirstOrDefaultAsync(ct);
    }

    public async Task<bool> ExistsAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        return await db.Stores.AnyAsync(ct);
    }

    public async Task AddAsync(Store store, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        db.Stores.Add(store);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Store store, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        store.UpdatedAt = DateTime.Now;
        db.Stores.Update(store);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<Announcement>> GetAnnouncementsAsync(
        Guid storeId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        return await db.Announcements
            .AsNoTracking()
            .Where(a => a.StoreId == storeId)
            .OrderBy(a => a.DisplayOrder)
            .ToListAsync(ct);
    }

    public async Task ReplaceAnnouncementsAsync(
        Guid storeId, IReadOnlyList<Announcement> announcements, CancellationToken ct = default)
    {
        if (announcements.Count > MaxAnnouncements)
        {
            throw new ArgumentException(
                $"お知らせは最大 {MaxAnnouncements} 件です。", nameof(announcements));
        }

        await using var db = await factory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var existing = await db.Announcements.Where(a => a.StoreId == storeId).ToListAsync(ct);
        db.Announcements.RemoveRange(existing);

        var now = DateTime.Now;
        var order = 1;

        foreach (var announcement in announcements)
        {
            announcement.StoreId = storeId;
            announcement.DisplayOrder = order++;
            announcement.CreatedAt = now;
            announcement.UpdatedAt = now;

            db.Announcements.Add(announcement);
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
}
