using Microsoft.EntityFrameworkCore;
using QTimeRecord.Core.Domain;

namespace QTimeRecord.Core.Data.Repositories;

public interface IDeviceSettingsRepository
{
    Task<DeviceSettings?> GetAsync(Guid storeId, CancellationToken ct = default);

    Task SaveAsync(DeviceSettings settings, CancellationToken ct = default);
}

public sealed class DeviceSettingsRepository(IDbContextFactory<QTimeRecordDbContext> factory)
    : IDeviceSettingsRepository
{
    public async Task<DeviceSettings?> GetAsync(Guid storeId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        return await db.DeviceSettings.AsNoTracking().FirstOrDefaultAsync(d => d.StoreId == storeId, ct);
    }

    public async Task SaveAsync(DeviceSettings settings, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        settings.UpdatedAt = DateTime.Now;

        var exists = await db.DeviceSettings.AnyAsync(d => d.StoreId == settings.StoreId, ct);
        if (exists)
        {
            db.DeviceSettings.Update(settings);
        }
        else
        {
            db.DeviceSettings.Add(settings);
        }

        await db.SaveChangesAsync(ct);
    }
}

public interface IAdminCredentialRepository
{
    Task<AdminCredential?> GetAsync(Guid storeId, CancellationToken ct = default);

    Task SaveAsync(AdminCredential credential, CancellationToken ct = default);
}

public sealed class AdminCredentialRepository(IDbContextFactory<QTimeRecordDbContext> factory)
    : IAdminCredentialRepository
{
    public async Task<AdminCredential?> GetAsync(Guid storeId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        return await db.AdminCredentials.AsNoTracking().FirstOrDefaultAsync(c => c.StoreId == storeId, ct);
    }

    public async Task SaveAsync(AdminCredential credential, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        credential.UpdatedAt = DateTime.Now;

        var exists = await db.AdminCredentials.AnyAsync(c => c.StoreId == credential.StoreId, ct);
        if (exists)
        {
            db.AdminCredentials.Update(credential);
        }
        else
        {
            db.AdminCredentials.Add(credential);
        }

        await db.SaveChangesAsync(ct);
    }
}
