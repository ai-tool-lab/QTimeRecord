using Microsoft.Extensions.Logging.Abstractions;
using QTimeRecord.Core.Data.Repositories;
using QTimeRecord.Core.Services;
using QTimeRecord.Tests.Data;

namespace QTimeRecord.Tests.Services;

public sealed class AdminAuthServiceTests
{
    private const string CorrectPin = "12345678";
    private const string WrongPin = "87654321";

    [Fact]
    public async Task 正しいPINで認証できる()
    {
        using var db = new TestDatabase();
        var fixture = await Fixture.CreateAsync(db);

        var outcome = await fixture.Service.AuthenticateAsync(CorrectPin);

        Assert.Equal(AdminAuthResult.Success, outcome.Result);
        Assert.True(outcome.IsSuccess);
    }

    [Fact]
    public async Task 誤ったPINでは認証できず残り回数が減る()
    {
        using var db = new TestDatabase();
        var fixture = await Fixture.CreateAsync(db);

        var outcome = await fixture.Service.AuthenticateAsync(WrongPin);

        Assert.Equal(AdminAuthResult.InvalidPin, outcome.Result);
        Assert.Equal(AdminAuthService.MaxFailedAttempts - 1, outcome.RemainingAttempts);
    }

    [Fact]
    public async Task 五回連続で失敗するとロックされる()
    {
        using var db = new TestDatabase();
        var fixture = await Fixture.CreateAsync(db);

        for (var i = 1; i < AdminAuthService.MaxFailedAttempts; i++)
        {
            var interim = await fixture.Service.AuthenticateAsync(WrongPin);
            Assert.Equal(AdminAuthResult.InvalidPin, interim.Result);
        }

        var outcome = await fixture.Service.AuthenticateAsync(WrongPin);

        Assert.Equal(AdminAuthResult.Locked, outcome.Result);
        Assert.Equal(AdminAuthService.LockDuration, outcome.LockRemaining);
    }

    [Fact]
    public async Task ロック中は正しいPINでも通さない()
    {
        using var db = new TestDatabase();
        var fixture = await Fixture.CreateAsync(db);

        await fixture.FailUntilLockedAsync();

        var outcome = await fixture.Service.AuthenticateAsync(CorrectPin);

        // ロック中に検証すると、総当たりの試行を許すことになる。
        Assert.Equal(AdminAuthResult.Locked, outcome.Result);
    }

    [Fact]
    public async Task ロックはアプリを再起動しても続く()
    {
        using var db = new TestDatabase();
        var fixture = await Fixture.CreateAsync(db);

        await fixture.FailUntilLockedAsync();

        // 起動し直したのと同じ状態（サービスを作り直し、DB だけを引き継ぐ）。
        // メモリにロックを置くと、ここで解除されてしまう。
        var restarted = fixture.NewService();

        var outcome = await restarted.AuthenticateAsync(CorrectPin);

        Assert.Equal(AdminAuthResult.Locked, outcome.Result);
    }

    [Fact]
    public async Task ロック期限が過ぎれば認証できる()
    {
        using var db = new TestDatabase();
        var fixture = await Fixture.CreateAsync(db);

        await fixture.FailUntilLockedAsync();

        fixture.Clock.Advance(AdminAuthService.LockDuration);

        var outcome = await fixture.Service.AuthenticateAsync(CorrectPin);

        Assert.Equal(AdminAuthResult.Success, outcome.Result);
    }

    [Fact]
    public async Task ロック解除の直前はまだ通さない()
    {
        using var db = new TestDatabase();
        var fixture = await Fixture.CreateAsync(db);

        await fixture.FailUntilLockedAsync();

        fixture.Clock.Advance(AdminAuthService.LockDuration - TimeSpan.FromSeconds(1));

        var outcome = await fixture.Service.AuthenticateAsync(CorrectPin);

        Assert.Equal(AdminAuthResult.Locked, outcome.Result);
        Assert.Equal(TimeSpan.FromSeconds(1), outcome.LockRemaining);
    }

    [Fact]
    public async Task ロック明けは失敗回数が数え直しになる()
    {
        using var db = new TestDatabase();
        var fixture = await Fixture.CreateAsync(db);

        await fixture.FailUntilLockedAsync();
        fixture.Clock.Advance(AdminAuthService.LockDuration);

        var outcome = await fixture.Service.AuthenticateAsync(WrongPin);

        // 引き継ぐと、ロック明けの1回の入力ミスで即座に再ロックされる。
        Assert.Equal(AdminAuthResult.InvalidPin, outcome.Result);
        Assert.Equal(AdminAuthService.MaxFailedAttempts - 1, outcome.RemainingAttempts);
    }

    [Fact]
    public async Task 四回失敗してから成功すると回数が戻る()
    {
        using var db = new TestDatabase();
        var fixture = await Fixture.CreateAsync(db);

        for (var i = 0; i < AdminAuthService.MaxFailedAttempts - 1; i++)
        {
            await fixture.Service.AuthenticateAsync(WrongPin);
        }

        Assert.Equal(AdminAuthResult.Success, (await fixture.Service.AuthenticateAsync(CorrectPin)).Result);

        // リセットされていないと、次の1回の入力ミスでロックされる。
        var outcome = await fixture.Service.AuthenticateAsync(WrongPin);

        Assert.Equal(AdminAuthResult.InvalidPin, outcome.Result);
        Assert.Equal(AdminAuthService.MaxFailedAttempts - 1, outcome.RemainingAttempts);
    }

    [Fact]
    public async Task 状態の確認では失敗回数を増やさない()
    {
        using var db = new TestDatabase();
        var fixture = await Fixture.CreateAsync(db);

        await fixture.Service.AuthenticateAsync(WrongPin);

        for (var i = 0; i < 10; i++)
        {
            var status = await fixture.Service.GetStatusAsync();
            Assert.False(status.IsLocked);
        }

        // 画面を開き直すだけでロックされてはいけない。
        Assert.Equal(AdminAuthResult.Success, (await fixture.Service.AuthenticateAsync(CorrectPin)).Result);
    }

    [Fact]
    public async Task ロック中は状態の確認で残り時間が分かる()
    {
        using var db = new TestDatabase();
        var fixture = await Fixture.CreateAsync(db);

        await fixture.FailUntilLockedAsync();
        fixture.Clock.Advance(TimeSpan.FromMinutes(2));

        var status = await fixture.Service.GetStatusAsync();

        Assert.True(status.IsLocked);
        Assert.Equal(TimeSpan.FromMinutes(3), status.LockRemaining);
    }

    [Fact]
    public async Task 未セットアップなら認証できない()
    {
        using var db = new TestDatabase();
        db.Migrate();

        var fixture = Fixture.WithoutStore(db);

        Assert.Equal(
            AdminAuthResult.NotConfigured, (await fixture.Service.AuthenticateAsync(CorrectPin)).Result);
        Assert.Equal(
            AdminAuthResult.NotConfigured, (await fixture.Service.GetStatusAsync()).Result);
    }

    // ---- 補助 ----

    private sealed class Fixture
    {
        private Fixture(TestDatabase db, TestClock clock)
        {
            Db = db;
            Clock = clock;
            Service = NewService();
        }

        public TestClock Clock { get; }

        public IAdminAuthService Service { get; }

        private TestDatabase Db { get; }

        public static async Task<Fixture> CreateAsync(TestDatabase db)
        {
            db.Migrate();

            var setup = new StoreSetupService(
                new StoreRepository(db.Factory),
                new AdminCredentialRepository(db.Factory),
                new DeviceSettingsRepository(db.Factory),
                new PasswordHasher());

            await setup.InitializeAsync(new StoreSetupRequest
            {
                CompanyName = "テスト株式会社",
                StoreName = "相模原店",
                BusinessDayStart = new TimeOnly(9, 0),
                BusinessDayEnd = new TimeOnly(22, 0),
                AdminPin = CorrectPin,
            });

            return new Fixture(db, new TestClock());
        }

        public static Fixture WithoutStore(TestDatabase db) => new(db, new TestClock());

        /// <summary>同じ DB を見る新しいサービス。アプリの再起動に相当する。</summary>
        public IAdminAuthService NewService() => new AdminAuthService(
            new StoreRepository(Db.Factory),
            new AdminCredentialRepository(Db.Factory),
            new PasswordHasher(),
            Clock,
            NullLogger<AdminAuthService>.Instance);

        public async Task FailUntilLockedAsync()
        {
            for (var i = 0; i < AdminAuthService.MaxFailedAttempts; i++)
            {
                await Service.AuthenticateAsync(WrongPin);
            }
        }
    }
}
