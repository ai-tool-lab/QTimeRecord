using Microsoft.Extensions.Logging;
using QTimeRecord.App.Services;
using QTimeRecord.App.ViewModels;
using QTimeRecord.Core.Data.Repositories;
using QTimeRecord.Core.Devices;
using QTimeRecord.Core.Domain;
using QTimeRecord.Core.Infrastructure;
using QTimeRecord.Core.Services;

namespace QTimeRecord.Tests.Infrastructure;

/// <summary>
/// ログに何が残り、何が残らないかを確かめる（→ plan.md 15-1 / 15-3）。
///
/// 残らないことの確認が本題。氏名や QR トークンが混ざっていても
/// 動作は変わらないため、テストで押さえないと気づけない。
/// </summary>
public sealed class LoggedContentTests
{
    private const string Token = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
    private const string StaffName = "山田 太郎";

    [Fact]
    public async Task 未登録のQRは指紋だけを残す()
    {
        var (viewModel, logger) = Create(QrResolveResult.Failed(QrResolution.NotFound));

        await viewModel.HandleScanAsync(Token);

        Assert.True(logger.Contains(LogSafe.TokenFingerprint(Token)));

        // トークンそのものが残ると、ログを見た人がその QR で打刻できる。
        Assert.DoesNotContain(logger.Messages, m => m.Contains(Token, StringComparison.Ordinal));
    }

    [Fact]
    public async Task 失効したQRはスタッフIDと失効日時を残す()
    {
        var staffId = Guid.CreateVersion7();
        var revokedAt = new DateTime(2026, 9, 1, 10, 0, 0);

        var (viewModel, logger) = Create(
            new QrResolveResult(QrResolution.Revoked, Staff: null, staffId, revokedAt));

        await viewModel.HandleScanAsync(Token);

        Assert.True(logger.Contains(staffId.ToString()));
        Assert.True(logger.Contains("失効"));
    }

    [Theory]
    [InlineData(QrResolution.OnLeave, StaffStatus.OnLeave)]
    [InlineData(QrResolution.Retired, StaffStatus.Retired)]
    public async Task 打刻できない在籍状態はスタッフIDを残す(
        QrResolution resolution, StaffStatus status)
    {
        var staff = NewStaff(status);
        var (viewModel, logger) = Create(new QrResolveResult(resolution, staff));

        await viewModel.HandleScanAsync(Token);

        Assert.True(logger.Contains(staff.Id.ToString()));
    }

    [Fact]
    public async Task 読み取り失敗でも必ずログに残す()
    {
        // 残さないと「かざしても打刻できない」の原因を後から追えない。
        foreach (var resolution in new[]
        {
            QrResolution.NotFound, QrResolution.Revoked,
            QrResolution.OnLeave, QrResolution.Retired,
        })
        {
            var (viewModel, logger) = Create(QrResolveResult.Failed(resolution));

            await viewModel.HandleScanAsync(Token);

            Assert.NotEmpty(logger.Entries);
        }
    }

    [Fact]
    public async Task 氏名はログに書かない()
    {
        var staff = NewStaff(StaffStatus.Active);
        var (viewModel, logger) = Create(new QrResolveResult(QrResolution.Ok, staff));

        await viewModel.HandleScanAsync(Token);

        // ログファイルが流出しても個人が特定されにくいようにする（→ CLAUDE.md 必須ルール6）。
        Assert.NotEmpty(logger.Entries);
        Assert.DoesNotContain(logger.Messages, m => m.Contains(StaffName, StringComparison.Ordinal));
        Assert.True(logger.Contains(staff.Id.ToString()));
    }

    [Fact]
    public void 長すぎる受信は長さだけを残す()
    {
        var logger = new CapturingLogger<QrScannerService>();
        var buffer = new ScanBuffer(maxLength: 16);

        buffer.Overflowed += (_, length) => logger.Log(
            LogLevel.Warning, default, $"受信が長すぎるため破棄しました。{length} 文字",
            null, (state, _) => state);

        var payload = new string('X', 40) + "\r";
        buffer.Append(System.Text.Encoding.ASCII.GetBytes(payload), DateTime.Now);

        Assert.True(logger.Contains("破棄しました"));

        // 読み取れなかったデータにも QR トークンが含まれうる。内容は残さない。
        Assert.DoesNotContain(
            logger.Messages, m => m.Contains("XXXXXXXXXXXXXXXXX", StringComparison.Ordinal));
    }

    [Fact]
    public void 上限を超えた受信は打刻に使わない()
    {
        var buffer = new ScanBuffer(maxLength: 16);

        var results = buffer.Append(
            System.Text.Encoding.ASCII.GetBytes(new string('X', 40) + "\r"), DateTime.Now);

        // 中途半端に切り詰めた文字列を打刻へ渡さない。
        Assert.Empty(results);
    }

    [Fact]
    public void 破棄したあとの正常な受信は読み取れる()
    {
        var buffer = new ScanBuffer(maxLength: 16);
        var now = DateTime.Now;

        buffer.Append(System.Text.Encoding.ASCII.GetBytes(new string('X', 40) + "\r"), now);

        var results = buffer.Append(System.Text.Encoding.ASCII.GetBytes("SHORT\r"), now);

        // 1件壊れただけで、以後ずっと読めなくなってはいけない。
        Assert.Equal("SHORT", Assert.Single(results));
    }

    // ---- 補助 ----

    private static (IdleViewModel ViewModel, CapturingLogger<IdleViewModel> Logger) Create(
        QrResolveResult result)
    {
        var logger = new CapturingLogger<IdleViewModel>();

        var viewModel = new IdleViewModel(
            new StubStoreRepository(),
            new StubScanner(),
            new StubTokenService(result),
            new TestClock(),
            new StubTicker(),
            logger);

        return (viewModel, logger);
    }

    private static Staff NewStaff(StaffStatus status) => new()
    {
        Id = Guid.CreateVersion7(),
        StoreId = Guid.CreateVersion7(),
        Name = StaffName,
        Status = status,
    };

    private sealed class StubTokenService(QrResolveResult result) : IQrTokenService
    {
        public string GenerateToken() => "STUB";

        public Task<QrResolveResult> ResolveAsync(string token, CancellationToken ct = default)
            => Task.FromResult(result);

        public Task<StaffQrToken> IssueAsync(
            Guid storeId, Guid staffId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<int> RevokeAsync(Guid staffId, CancellationToken ct = default)
            => Task.FromResult(0);
    }

    private sealed class StubScanner : IQrScannerService
    {
        public ScannerState State => ScannerState.Connected;

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

        public void Raise()
        {
            Scanned?.Invoke(this, "TOKEN");
            StateChanged?.Invoke(this, ScannerState.Connected);
        }
    }

    private sealed class StubStoreRepository : IStoreRepository
    {
        public Task<Store?> GetAsync(CancellationToken ct = default) => Task.FromResult<Store?>(null);

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
