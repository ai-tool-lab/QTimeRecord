using Microsoft.Extensions.Logging.Abstractions;
using QTimeRecord.App.Services;
using QTimeRecord.App.ViewModels;
using QTimeRecord.Core.Data.Repositories;
using QTimeRecord.Core.Devices;
using QTimeRecord.Core.Domain;
using QTimeRecord.Core.Services;

namespace QTimeRecord.Tests.ViewModels;

public sealed class IdleViewModelTests
{
    [Fact]
    public async Task 有効なQRならスタッフを特定して通知する()
    {
        var staff = NewStaff(StaffStatus.Active);
        var viewModel = Create(new QrResolveResult(QrResolution.Ok, staff));

        Staff? identified = null;
        viewModel.StaffIdentified += (_, s) => identified = s;

        await viewModel.HandleScanAsync("TOKEN");

        Assert.Same(staff, identified);
        Assert.Null(viewModel.ErrorMessage);
    }

    [Theory]
    [InlineData(QrResolution.NotFound, "登録されていない")]
    [InlineData(QrResolution.Revoked, "無効")]
    [InlineData(QrResolution.OnLeave, "休職中")]
    [InlineData(QrResolution.Retired, "退職済み")]
    public async Task 失敗の理由ごとに案内が変わる(QrResolution resolution, string expected)
    {
        var viewModel = Create(QrResolveResult.Failed(resolution));

        Staff? identified = null;
        viewModel.StaffIdentified += (_, s) => identified = s;

        await viewModel.HandleScanAsync("TOKEN");

        // 「読み取れません」で一括りにすると、スタッフが何をすればよいか分からない。
        Assert.NotNull(viewModel.ErrorMessage);
        Assert.Contains(expected, viewModel.ErrorMessage, StringComparison.Ordinal);
        Assert.Null(identified);
    }

    [Fact]
    public async Task エラーは一定時間で自動的に消える()
    {
        var clock = new TestClock();
        var ticker = new StubTicker();
        var viewModel = Create(QrResolveResult.Failed(QrResolution.NotFound), ticker, clock);

        await viewModel.HandleScanAsync("TOKEN");
        Assert.NotNull(viewModel.ErrorMessage);

        // 表示しっぱなしにすると、次の人が前の人のエラーを自分のものだと思う。
        clock.Advance(IdleViewModel.ErrorDisplayDuration);
        ticker.Raise();

        Assert.Null(viewModel.ErrorMessage);
    }

    [Fact]
    public async Task 表示時間内はエラーが残る()
    {
        var clock = new TestClock();
        var ticker = new StubTicker();
        var viewModel = Create(QrResolveResult.Failed(QrResolution.NotFound), ticker, clock);

        await viewModel.HandleScanAsync("TOKEN");

        clock.Advance(IdleViewModel.ErrorDisplayDuration - TimeSpan.FromSeconds(1));
        ticker.Raise();

        Assert.NotNull(viewModel.ErrorMessage);
    }

    [Fact]
    public void 破棄すると読み取りに反応しなくなる()
    {
        var scanner = new StubScanner();
        var ticker = new StubTicker();
        var viewModel = Create(
            QrResolveResult.Failed(QrResolution.NotFound), ticker, new TestClock(), scanner);

        viewModel.Dispose();
        scanner.RaiseScanned("TOKEN");

        // 購読が残ると、画面を離れたあとも裏で反応してしまう。
        Assert.Null(viewModel.ErrorMessage);
    }

    [Fact]
    public async Task 読み取れなかったときも案内を出す()
    {
        var scanner = new StubScanner();
        using var viewModel = Create(
            QrResolveResult.Failed(QrResolution.NotFound),
            new StubTicker(), new TestClock(), scanner);

        scanner.RaiseScanFailed();

        // 何も出さないと、かざした人は端末が壊れていると受け取る。
        Assert.NotNull(viewModel.ErrorMessage);
        Assert.Contains("もう一度", viewModel.ErrorMessage, StringComparison.Ordinal);

        await Task.CompletedTask;
    }

    [Fact]
    public void 接続が切れたら理由を出す()
    {
        var scanner = new StubScanner();
        using var viewModel = Create(
            QrResolveResult.Failed(QrResolution.NotFound),
            new StubTicker(), new TestClock(), scanner);

        Assert.Null(viewModel.ScannerNotice);

        scanner.RaiseStateChanged(ScannerState.Disconnected);

        // 色の点だけでは、かざしても反応しない理由が伝わらない。
        Assert.NotNull(viewModel.ScannerNotice);
        Assert.Contains("再接続", viewModel.ScannerNotice, StringComparison.Ordinal);
    }

    [Fact]
    public void 破棄すると読み取り失敗にも反応しなくなる()
    {
        var scanner = new StubScanner();
        var viewModel = Create(
            QrResolveResult.Failed(QrResolution.NotFound),
            new StubTicker(), new TestClock(), scanner);

        viewModel.Dispose();
        scanner.RaiseScanFailed();

        Assert.Null(viewModel.ErrorMessage);
    }

    [Fact]
    public void 接続状態が表示文言に反映される()
    {
        var scanner = new StubScanner();
        var viewModel = Create(
            QrResolveResult.Failed(QrResolution.NotFound),
            new StubTicker(), new TestClock(), scanner);

        scanner.RaiseStateChanged(ScannerState.Connected);
        Assert.Equal("リーダー待機中", viewModel.ScannerStatusText);
        Assert.True(viewModel.IsScannerReady);

        scanner.RaiseStateChanged(ScannerState.Disconnected);
        Assert.Equal("リーダー未接続", viewModel.ScannerStatusText);
        Assert.False(viewModel.IsScannerReady);
    }

    // ---- 補助 ----

    private static IdleViewModel Create(QrResolveResult result)
        => Create(result, new StubTicker(), new TestClock());

    private static IdleViewModel Create(
        QrResolveResult result, StubTicker ticker, TestClock clock, StubScanner? scanner = null)
        => new(
            new StubStoreRepository(),
            scanner ?? new StubScanner(),
            new StubTokenService(result),
            clock,
            ticker,
            NullLogger<IdleViewModel>.Instance);

    private static Staff NewStaff(StaffStatus status) => new()
    {
        Id = Guid.CreateVersion7(),
        StoreId = Guid.CreateVersion7(),
        Name = "山田 太郎",
        Status = status,
    };

    private sealed class StubScanner : IQrScannerService
    {
        public ScannerState State { get; private set; } = ScannerState.Connected;

        public event EventHandler<string>? Scanned;

        public event EventHandler<ScannerState>? StateChanged;

        public event EventHandler? ScanFailed;

        public void RaiseScanFailed() => ScanFailed?.Invoke(this, EventArgs.Empty);

        public void Start(SerialPortSettings settings)
        {
        }

        public void Stop()
        {
        }

        public void Tick()
        {
        }

        public Task<ConnectionTestResult> TestConnectionAsync(
            SerialPortSettings settings, TimeSpan timeout, CancellationToken ct = default)
            => Task.FromResult(new ConnectionTestResult(true, "ok"));

        public void Dispose()
        {
        }

        public void RaiseScanned(string token) => Scanned?.Invoke(this, token);

        public void RaiseStateChanged(ScannerState state)
        {
            State = state;
            StateChanged?.Invoke(this, state);
        }
    }

    private sealed class StubTokenService(QrResolveResult result) : IQrTokenService
    {
        public string GenerateToken() => "STUB";

        public Task<QrResolveResult> ResolveAsync(string token, CancellationToken ct = default)
            => Task.FromResult(result);

        public Task<StaffQrToken> IssueAsync(Guid storeId, Guid staffId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<int> RevokeAsync(Guid staffId, CancellationToken ct = default)
            => Task.FromResult(0);
    }

    private sealed class StubStoreRepository : IStoreRepository
    {
        public Task<Store?> GetAsync(CancellationToken ct = default)
            => Task.FromResult<Store?>(null);

        public Task<bool> ExistsAsync(CancellationToken ct = default) => Task.FromResult(false);

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
