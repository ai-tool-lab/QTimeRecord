using Microsoft.Extensions.Logging.Abstractions;
using QTimeRecord.App.Services;
using QTimeRecord.App.ViewModels;
using QTimeRecord.Core.Data.Repositories;
using QTimeRecord.Core.Domain;
using QTimeRecord.Core.Services;

namespace QTimeRecord.Tests.ViewModels;

public sealed class AdminLoginViewModelTests
{
    private const string Pin = "12345678";

    [Fact]
    public async Task 店舗名を表示する()
    {
        var harness = new Harness();

        using var viewModel = harness.Create();
        await viewModel.LoadAsync();

        Assert.Equal("相模原店", viewModel.StoreName);
        Assert.Equal("テスト株式会社", viewModel.CompanyName);
    }

    [Fact]
    public async Task 八桁そろうまでログインできない()
    {
        var harness = new Harness();

        using var viewModel = harness.Create();
        await viewModel.LoadAsync();

        viewModel.Pin = "1234567";
        Assert.False(viewModel.CanSubmit);
        Assert.False(viewModel.SubmitCommand.CanExecute(null));

        viewModel.Pin = Pin;
        Assert.True(viewModel.CanSubmit);
        Assert.True(viewModel.SubmitCommand.CanExecute(null));
    }

    [Fact]
    public async Task 入力は伏せ字で桁数だけ分かる()
    {
        var harness = new Harness();

        using var viewModel = harness.Create();
        await viewModel.LoadAsync();

        viewModel.Pin = "123";

        Assert.Equal("●●●", viewModel.MaskedPin);
        Assert.Equal("3 / 8 桁", viewModel.PinIndicator);
    }

    [Fact]
    public async Task 認証できたら通知する()
    {
        var harness = new Harness();
        harness.Auth.Outcomes.Enqueue(Harness.Success);

        using var viewModel = harness.Create();
        await viewModel.LoadAsync();

        var authenticated = false;
        viewModel.Authenticated += (_, _) => authenticated = true;

        viewModel.Pin = Pin;
        await viewModel.SubmitAsync();

        Assert.True(authenticated);
    }

    [Fact]
    public async Task 失敗したら残り回数を伝えて入力を消す()
    {
        var harness = new Harness();
        harness.Auth.Outcomes.Enqueue(new AdminAuthOutcome(AdminAuthResult.InvalidPin, 3, TimeSpan.Zero));

        using var viewModel = harness.Create();
        await viewModel.LoadAsync();

        viewModel.Pin = Pin;
        await viewModel.SubmitAsync();

        Assert.Contains("あと 3 回", viewModel.ErrorMessage!, StringComparison.Ordinal);

        // 残すと、次に触った人が続きから試せてしまう。
        Assert.Equal(string.Empty, viewModel.Pin);
    }

    [Fact]
    public async Task ロック中は入力できず残り時間を出す()
    {
        var harness = new Harness();
        harness.Auth.Status = new AdminAuthOutcome(
            AdminAuthResult.Locked, 0, TimeSpan.FromMinutes(5));

        using var viewModel = harness.Create();
        await viewModel.LoadAsync();

        Assert.True(viewModel.IsLocked);
        Assert.False(viewModel.CanSubmit);
        Assert.Contains("5分00秒", viewModel.LockMessage!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ロックの残り時間が減っていく()
    {
        var harness = new Harness();
        harness.Auth.Status = new AdminAuthOutcome(
            AdminAuthResult.Locked, 0, TimeSpan.FromMinutes(5));

        using var viewModel = harness.Create();
        await viewModel.LoadAsync();

        harness.Clock.Advance(TimeSpan.FromMinutes(2));
        harness.Ticker.Raise();

        Assert.Contains("3分00秒", viewModel.LockMessage!, StringComparison.Ordinal);

        // 残り時間の計算に DB を使うと、100ms ごとの読み取りが5分間続く。
        Assert.Equal(1, harness.Auth.StatusCalls);
    }

    [Fact]
    public async Task ロックが明けたら入力できるようになる()
    {
        var harness = new Harness();
        harness.Auth.Status = new AdminAuthOutcome(
            AdminAuthResult.Locked, 0, TimeSpan.FromMinutes(5));

        using var viewModel = harness.Create();
        await viewModel.LoadAsync();

        harness.Clock.Advance(TimeSpan.FromMinutes(5));
        harness.Ticker.Raise();

        Assert.False(viewModel.IsLocked);
        Assert.Null(viewModel.LockMessage);

        viewModel.Pin = Pin;
        Assert.True(viewModel.CanSubmit);
    }

    [Fact]
    public async Task 無操作が続くと待機へ戻る()
    {
        var harness = new Harness();

        using var viewModel = harness.Create();
        await viewModel.LoadAsync();

        var cancelled = false;
        viewModel.Cancelled += (_, _) => cancelled = true;

        harness.Clock.Advance(AdminLoginViewModel.InputTimeout);
        harness.Ticker.Raise();

        // 開いたまま人が離れると、誰も打刻できない端末になる。
        Assert.True(cancelled);
    }

    [Fact]
    public async Task 入力している間は待機へ戻らない()
    {
        var harness = new Harness();

        using var viewModel = harness.Create();
        await viewModel.LoadAsync();

        var cancelled = false;
        viewModel.Cancelled += (_, _) => cancelled = true;

        for (var i = 0; i < 4; i++)
        {
            harness.Clock.Advance(AdminLoginViewModel.InputTimeout - TimeSpan.FromSeconds(1));
            harness.Ticker.Raise();
            viewModel.Pin += "1";
        }

        Assert.False(cancelled);
    }

    [Fact]
    public async Task 破棄するとタイマーが残らない()
    {
        var harness = new Harness();

        var viewModel = harness.Create();
        await viewModel.LoadAsync();

        viewModel.Dispose();

        Assert.Equal(0, harness.Ticker.SubscriberCount);
    }

    [Fact]
    public void ロックの残り時間を分と秒で書く()
    {
        Assert.Contains("4分12秒", AdminLoginViewModel.DescribeLock(TimeSpan.FromSeconds(252)),
            StringComparison.Ordinal);
        Assert.Contains("0分05秒", AdminLoginViewModel.DescribeLock(TimeSpan.FromSeconds(5)),
            StringComparison.Ordinal);
    }

    // ---- 補助 ----

    private sealed class Harness
    {
        public static readonly AdminAuthOutcome Success =
            new(AdminAuthResult.Success, AdminAuthService.MaxFailedAttempts, TimeSpan.Zero);

        public StubAuthService Auth { get; } = new();

        public StubTicker Ticker { get; } = new();

        public TestClock Clock { get; } = new();

        public AdminLoginViewModel Create() => new(
            Auth,
            new StubStoreRepository(),
            new IdleTimeoutService(Clock, Ticker),
            Ticker,
            Clock,
            NullLogger<AdminLoginViewModel>.Instance);
    }

    private sealed class StubAuthService : IAdminAuthService
    {
        public AdminAuthOutcome Status { get; set; } =
            new(AdminAuthResult.InvalidPin, AdminAuthService.MaxFailedAttempts, TimeSpan.Zero);

        public Queue<AdminAuthOutcome> Outcomes { get; } = new();

        public int StatusCalls { get; private set; }

        public Task<AdminAuthOutcome> GetStatusAsync(CancellationToken ct = default)
        {
            StatusCalls++;
            return Task.FromResult(Status);
        }

        public Task<AdminAuthOutcome> AuthenticateAsync(string pin, CancellationToken ct = default)
            => Task.FromResult(Outcomes.Count > 0 ? Outcomes.Dequeue() : Status);
    }

    private sealed class StubStoreRepository : IStoreRepository
    {
        public Task<Store?> GetAsync(CancellationToken ct = default) => Task.FromResult<Store?>(new Store
        {
            Id = Guid.CreateVersion7(),
            CompanyName = "テスト株式会社",
            StoreName = "相模原店",
            BusinessDayStart = new TimeOnly(9, 0),
            BusinessDayEnd = new TimeOnly(22, 0),
        });

        public Task<bool> ExistsAsync(CancellationToken ct = default) => Task.FromResult(true);

        public Task AddAsync(Store store, CancellationToken ct = default) => Task.CompletedTask;

        public Task UpdateAsync(Store store, CancellationToken ct = default) => Task.CompletedTask;

        public Task<IReadOnlyList<Announcement>> GetAnnouncementsAsync(
            Guid storeId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Announcement>>([]);

        public Task ReplaceAnnouncementsAsync(
            Guid storeId, IReadOnlyList<Announcement> announcements, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
