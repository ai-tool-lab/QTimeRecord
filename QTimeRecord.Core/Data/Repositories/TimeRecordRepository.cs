using Microsoft.EntityFrameworkCore;
using QTimeRecord.Core.Domain;

namespace QTimeRecord.Core.Data.Repositories;

public interface ITimeRecordRepository
{
    Task AddAsync(TimeRecord record, CancellationToken ct = default);

    Task<TimeRecord?> GetByIdAsync(Guid recordId, CancellationToken ct = default);

    Task UpdateAsync(TimeRecord record, CancellationToken ct = default);

    Task DeleteAsync(Guid recordId, CancellationToken ct = default);

    /// <summary>指定した営業月の打刻。勤務状況の一覧と CSV 出力で使う。</summary>
    Task<IReadOnlyList<TimeRecord>> ListByMonthAsync(
        Guid storeId, int year, int month, Guid? staffId = null, CancellationToken ct = default);

    /// <summary>ある営業日の、あるスタッフの打刻。打刻状態の判定に使う。</summary>
    Task<IReadOnlyList<TimeRecord>> ListByWorkDateAsync(
        Guid storeId, Guid staffId, DateOnly workDate, CancellationToken ct = default);

    /// <summary>直近の打刻1件。打刻画面に「前回」を出して誤打刻に気づけるようにする。</summary>
    Task<TimeRecord?> GetLatestAsync(Guid staffId, CancellationToken ct = default);

    /// <summary>
    /// 未退勤の出勤打刻を探す。営業時間外の打刻をどちらの営業日に付けるかの判定に使う（→ plan.md Q2-a）。
    /// 直近の出勤より後に退勤が無ければ、その出勤を返す。
    /// </summary>
    Task<TimeRecord?> GetOpenClockInAsync(Guid staffId, CancellationToken ct = default);
}

public sealed class TimeRecordRepository(IDbContextFactory<QTimeRecordDbContext> factory)
    : ITimeRecordRepository
{
    public async Task AddAsync(TimeRecord record, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var now = DateTime.Now;
        record.CreatedAt = now;
        record.UpdatedAt = now;

        db.TimeRecords.Add(record);
        await db.SaveChangesAsync(ct);
    }

    public async Task<TimeRecord?> GetByIdAsync(Guid recordId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        return await db.TimeRecords.AsNoTracking().FirstOrDefaultAsync(r => r.Id == recordId, ct);
    }

    public async Task UpdateAsync(TimeRecord record, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        record.UpdatedAt = DateTime.Now;
        db.TimeRecords.Update(record);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid recordId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        await db.TimeRecords.Where(r => r.Id == recordId).ExecuteDeleteAsync(ct);
    }

    public async Task<IReadOnlyList<TimeRecord>> ListByMonthAsync(
        Guid storeId, int year, int month, Guid? staffId = null, CancellationToken ct = default)
    {
        // 月初と月末は営業日で切る。カレンダー日付ではないので、
        // recorded_at で範囲を取ると深夜勤務が隣の月へこぼれる。
        var from = new DateOnly(year, month, 1);
        var to = from.AddMonths(1).AddDays(-1);

        await using var db = await factory.CreateDbContextAsync(ct);

        var query = db.TimeRecords
            .AsNoTracking()
            .Include(r => r.Staff)
            .Where(r => r.StoreId == storeId && r.WorkDate >= from && r.WorkDate <= to);

        if (staffId is not null)
        {
            query = query.Where(r => r.StaffId == staffId);
        }

        return await query
            .OrderBy(r => r.WorkDate)
            .ThenBy(r => r.RecordedAt)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<TimeRecord>> ListByWorkDateAsync(
        Guid storeId, Guid staffId, DateOnly workDate, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        return await db.TimeRecords
            .AsNoTracking()
            .Where(r => r.StoreId == storeId && r.StaffId == staffId && r.WorkDate == workDate)
            .OrderBy(r => r.RecordedAt)
            .ToListAsync(ct);
    }

    public async Task<TimeRecord?> GetLatestAsync(Guid staffId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        return await db.TimeRecords
            .AsNoTracking()
            .Where(r => r.StaffId == staffId)
            .OrderByDescending(r => r.RecordedAt)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<TimeRecord?> GetOpenClockInAsync(Guid staffId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var lastClockIn = await db.TimeRecords
            .AsNoTracking()
            .Where(r => r.StaffId == staffId && r.RecordType == TimeRecordType.ClockIn)
            .OrderByDescending(r => r.RecordedAt)
            .FirstOrDefaultAsync(ct);

        if (lastClockIn is null)
        {
            return null;
        }

        var hasClockOutAfter = await db.TimeRecords
            .AsNoTracking()
            .AnyAsync(
                r => r.StaffId == staffId
                    && r.RecordType == TimeRecordType.ClockOut
                    && r.RecordedAt > lastClockIn.RecordedAt,
                ct);

        return hasClockOutAfter ? null : lastClockIn;
    }
}
