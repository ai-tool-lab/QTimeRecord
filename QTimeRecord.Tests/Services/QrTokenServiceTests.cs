using QTimeRecord.Core.Data.Repositories;
using QTimeRecord.Core.Domain;
using QTimeRecord.Core.Services;
using QTimeRecord.Tests.Data;

namespace QTimeRecord.Tests.Services;

public sealed class QrTokenServiceTests
{
    // ---- トークンの生成 ----

    [Fact]
    public void トークンは規定の長さと文字種になる()
    {
        var service = new QrTokenService(new StubTokenRepository(), new TestClock());

        var token = service.GenerateToken();

        Assert.Equal(QrTokenService.TokenLength, token.Length);
        Assert.Matches("^[A-Z2-7]{32}$", token);
    }

    [Fact]
    public void トークンは毎回異なる()
    {
        var service = new QrTokenService(new StubTokenRepository(), new TestClock());

        var tokens = Enumerable.Range(0, 200).Select(_ => service.GenerateToken()).ToHashSet();

        // 推測できる規則性（連番・社員番号由来）があると、他人のQRを作れてしまう。
        Assert.Equal(200, tokens.Count);
    }

    // ---- 読み取り結果の判定 ----

    [Fact]
    public async Task 有効なQRなら打刻できる()
    {
        using var db = CreateDatabase(out var storeId, out var staffId, StaffStatus.Active);
        var (service, tokens) = CreateService(db);

        var issued = await service.IssueAsync(storeId, staffId);
        var result = await service.ResolveAsync(issued.Token);

        Assert.Equal(QrResolution.Ok, result.Resolution);
        Assert.True(result.CanPunch);
        Assert.NotNull(result.Staff);
        Assert.Equal(staffId, result.Staff.Id);
        Assert.NotNull(await tokens.GetActiveForStaffAsync(staffId));
    }

    [Fact]
    public async Task 未登録のQRは登録なしと判定される()
    {
        using var db = CreateDatabase(out _, out _, StaffStatus.Active);
        var (service, _) = CreateService(db);

        var result = await service.ResolveAsync("ZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZ");

        Assert.Equal(QrResolution.NotFound, result.Resolution);
        Assert.False(result.CanPunch);
    }

    [Fact]
    public async Task 失効したQRは未登録と区別される()
    {
        using var db = CreateDatabase(out var storeId, out var staffId, StaffStatus.Active);
        var (service, _) = CreateService(db);

        var old = await service.IssueAsync(storeId, staffId);
        await service.IssueAsync(storeId, staffId);

        var result = await service.ResolveAsync(old.Token);

        // 「登録されていません」と「無効です。管理者へ連絡してください」では案内が違う。
        Assert.Equal(QrResolution.Revoked, result.Resolution);
    }

    [Theory]
    [InlineData(StaffStatus.OnLeave, QrResolution.OnLeave)]
    [InlineData(StaffStatus.Retired, QrResolution.Retired)]
    public async Task 在籍状態に応じて拒否される(StaffStatus status, QrResolution expected)
    {
        using var db = CreateDatabase(out var storeId, out var staffId, status);
        var (service, _) = CreateService(db);

        var issued = await service.IssueAsync(storeId, staffId);
        var result = await service.ResolveAsync(issued.Token);

        Assert.Equal(expected, result.Resolution);
        Assert.False(result.CanPunch);

        // 誰の QR かは分かる。管理者への連絡時に本人を特定できるようにするため。
        Assert.NotNull(result.Staff);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task 空の入力は未登録として扱う(string? token)
    {
        using var db = CreateDatabase(out _, out _, StaffStatus.Active);
        var (service, _) = CreateService(db);

        Assert.Equal(QrResolution.NotFound, (await service.ResolveAsync(token!)).Resolution);
    }

    [Fact]
    public async Task 前後の空白があっても読み取れる()
    {
        using var db = CreateDatabase(out var storeId, out var staffId, StaffStatus.Active);
        var (service, _) = CreateService(db);

        var issued = await service.IssueAsync(storeId, staffId);

        Assert.Equal(QrResolution.Ok, (await service.ResolveAsync($"  {issued.Token} ")).Resolution);
    }

    // ---- 失効 ----

    [Fact]
    public async Task 失効させると打刻できなくなる()
    {
        using var db = CreateDatabase(out var storeId, out var staffId, StaffStatus.Active);
        var (service, _) = CreateService(db);

        var issued = await service.IssueAsync(storeId, staffId);
        var revoked = await service.RevokeAsync(staffId);

        Assert.Equal(1, revoked);
        Assert.Equal(QrResolution.Revoked, (await service.ResolveAsync(issued.Token)).Resolution);
    }

    // ---- 補助 ----

    private static (QrTokenService Service, IQrTokenRepository Tokens) CreateService(TestDatabase db)
    {
        var tokens = new QrTokenRepository(db.Factory);

        return (new QrTokenService(tokens, new TestClock()), tokens);
    }

    private static TestDatabase CreateDatabase(out Guid storeId, out Guid staffId, StaffStatus status)
    {
        var db = new TestDatabase();
        db.Migrate();

        storeId = Guid.CreateVersion7();
        staffId = Guid.CreateVersion7();

        db.Context.Stores.Add(new Store
        {
            Id = storeId,
            StoreName = "テスト店",
            CompanyName = "テスト株式会社",
            BusinessDayStart = new TimeOnly(11, 0),
            BusinessDayEnd = new TimeOnly(5, 0),
        });

        db.Context.Staff.Add(new Staff
        {
            Id = staffId,
            StoreId = storeId,
            Name = "山田 太郎",
            Status = status,
        });

        db.Context.SaveChanges();

        return db;
    }

    /// <summary>生成だけを試すときに DB を用意しないためのもの。</summary>
    private sealed class StubTokenRepository : IQrTokenRepository
    {
        public Task<StaffQrToken?> FindByTokenAsync(string token, CancellationToken ct = default)
            => Task.FromResult<StaffQrToken?>(null);

        public Task<StaffQrToken?> GetActiveForStaffAsync(Guid staffId, CancellationToken ct = default)
            => Task.FromResult<StaffQrToken?>(null);

        public Task<IReadOnlyList<StaffQrToken>> ListForStaffAsync(
            Guid staffId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<StaffQrToken>>([]);

        public Task<StaffQrToken> IssueAsync(
            Guid storeId, Guid staffId, string token, DateTime issuedAt, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<int> RevokeActiveAsync(Guid staffId, DateTime revokedAt, CancellationToken ct = default)
            => Task.FromResult(0);
    }
}
