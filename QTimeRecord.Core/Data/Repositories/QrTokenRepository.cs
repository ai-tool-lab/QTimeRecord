using Microsoft.EntityFrameworkCore;
using QTimeRecord.Core.Domain;

namespace QTimeRecord.Core.Data.Repositories;

public interface IQrTokenRepository
{
    /// <summary>
    /// トークン文字列で引く。<b>失効済みも返す。</b>
    ///
    /// 「登録されていない QR」と「無効になった QR」では利用者への案内が違うため、
    /// 失効済みをここで除外せず、呼び出し側で判断させる。
    /// </summary>
    Task<StaffQrToken?> FindByTokenAsync(string token, CancellationToken ct = default);

    /// <summary>そのスタッフの有効なトークン。無ければ null。</summary>
    Task<StaffQrToken?> GetActiveForStaffAsync(Guid staffId, CancellationToken ct = default);

    Task<IReadOnlyList<StaffQrToken>> ListForStaffAsync(Guid staffId, CancellationToken ct = default);

    /// <summary>
    /// 有効なトークンをすべて失効させ、新しいトークンを1つ発行する。
    /// 「失効し忘れた古いQRで打刻できる」状態を作らないよう、1つのトランザクションで行う。
    /// </summary>
    Task<StaffQrToken> IssueAsync(
        Guid storeId, Guid staffId, string token, DateTime issuedAt, CancellationToken ct = default);

    /// <summary>有効なトークンをすべて失効させる（退職時など）。</summary>
    Task<int> RevokeActiveAsync(Guid staffId, DateTime revokedAt, CancellationToken ct = default);
}

public sealed class QrTokenRepository(IDbContextFactory<QTimeRecordDbContext> factory) : IQrTokenRepository
{
    public async Task<StaffQrToken?> FindByTokenAsync(string token, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        return await db.StaffQrTokens
            .AsNoTracking()
            .Include(t => t.Staff)
            .FirstOrDefaultAsync(t => t.Token == token, ct);
    }

    public async Task<StaffQrToken?> GetActiveForStaffAsync(Guid staffId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        return await db.StaffQrTokens
            .AsNoTracking()
            .Where(t => t.StaffId == staffId && t.RevokedAt == null)
            .OrderByDescending(t => t.IssuedAt)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<StaffQrToken>> ListForStaffAsync(
        Guid staffId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        return await db.StaffQrTokens
            .AsNoTracking()
            .Where(t => t.StaffId == staffId)
            .OrderByDescending(t => t.IssuedAt)
            .ToListAsync(ct);
    }

    public async Task<StaffQrToken> IssueAsync(
        Guid storeId, Guid staffId, string token, DateTime issuedAt, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        await RevokeActiveCoreAsync(db, staffId, issuedAt, ct);

        var issued = new StaffQrToken
        {
            Id = Guid.CreateVersion7(),
            StoreId = storeId,
            StaffId = staffId,
            Token = token,
            IssuedAt = issuedAt,
            CreatedAt = issuedAt,
            UpdatedAt = issuedAt,
        };

        db.StaffQrTokens.Add(issued);

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return issued;
    }

    public async Task<int> RevokeActiveAsync(
        Guid staffId, DateTime revokedAt, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var count = await RevokeActiveCoreAsync(db, staffId, revokedAt, ct);
        await db.SaveChangesAsync(ct);

        return count;
    }

    private static async Task<int> RevokeActiveCoreAsync(
        QTimeRecordDbContext db, Guid staffId, DateTime revokedAt, CancellationToken ct)
    {
        var active = await db.StaffQrTokens
            .Where(t => t.StaffId == staffId && t.RevokedAt == null)
            .ToListAsync(ct);

        foreach (var token in active)
        {
            token.RevokedAt = revokedAt;
            token.UpdatedAt = revokedAt;
        }

        return active.Count;
    }
}
