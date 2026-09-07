using Microsoft.EntityFrameworkCore;
using QTimeRecord.Core.Domain;

namespace QTimeRecord.Core.Data.Repositories;

public interface IStaffRepository
{
    Task<Staff?> GetByIdAsync(Guid staffId, CancellationToken ct = default);

    /// <summary>名簿。氏名・フリガナ・社員番号の部分一致で絞り込める。</summary>
    Task<IReadOnlyList<Staff>> ListAsync(
        Guid storeId,
        StaffStatus? status = null,
        string? search = null,
        CancellationToken ct = default);

    Task AddAsync(Staff staff, CancellationToken ct = default);

    Task UpdateAsync(Staff staff, CancellationToken ct = default);

    /// <summary>社員番号の重複確認。編集中の本人は除外する。</summary>
    Task<bool> StaffNoExistsAsync(
        Guid storeId, string staffNo, Guid? excludeStaffId = null, CancellationToken ct = default);
}

public sealed class StaffRepository(IDbContextFactory<QTimeRecordDbContext> factory) : IStaffRepository
{
    public async Task<Staff?> GetByIdAsync(Guid staffId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        return await db.Staff.AsNoTracking().FirstOrDefaultAsync(s => s.Id == staffId, ct);
    }

    public async Task<IReadOnlyList<Staff>> ListAsync(
        Guid storeId,
        StaffStatus? status = null,
        string? search = null,
        CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var query = db.Staff.AsNoTracking().Where(s => s.StoreId == storeId);

        if (status is not null)
        {
            query = query.Where(s => s.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var keyword = search.Trim();

            query = query.Where(s =>
                s.Name.Contains(keyword)
                || (s.NameKana != null && s.NameKana.Contains(keyword))
                || (s.StaffNo != null && s.StaffNo.Contains(keyword)));
        }

        // 退職者を下へ送り、同じ状態のなかではフリガナ順。氏名だけだと漢字コード順になり探しにくい。
        return await query
            .OrderBy(s => s.Status)
            .ThenBy(s => s.NameKana ?? s.Name)
            .ToListAsync(ct);
    }

    public async Task AddAsync(Staff staff, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var now = DateTime.Now;
        staff.CreatedAt = now;
        staff.UpdatedAt = now;

        db.Staff.Add(staff);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Staff staff, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        staff.UpdatedAt = DateTime.Now;
        db.Staff.Update(staff);
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> StaffNoExistsAsync(
        Guid storeId, string staffNo, Guid? excludeStaffId = null, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        return await db.Staff.AnyAsync(
            s => s.StoreId == storeId
                && s.StaffNo == staffNo
                && (excludeStaffId == null || s.Id != excludeStaffId),
            ct);
    }
}
