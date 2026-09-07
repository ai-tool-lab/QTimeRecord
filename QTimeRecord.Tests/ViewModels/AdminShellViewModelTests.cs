using Microsoft.Extensions.Logging.Abstractions;
using QTimeRecord.App.Services;
using QTimeRecord.App.ViewModels;
using QTimeRecord.Core.Devices;

namespace QTimeRecord.Tests.ViewModels;

public sealed class AdminShellViewModelTests
{
    [Fact]
    public void 初期タブは勤務状況()
    {
        using var viewModel = Create(out _);

        // 管理者が最初に見たいのは今日の勤怠（→ plan.md 10-3-4）。
        Assert.Equal("勤務状況", viewModel.SelectedTab?.Title);
    }

    [Fact]
    public void タブは三つ()
    {
        using var viewModel = Create(out _);

        Assert.Equal(
            new[] { "勤務状況", "スタッフ管理", "店舗設定" },
            viewModel.Tabs.Select(t => t.Title));
    }

    [Fact]
    public void タブを切り替えると中身が変わる()
    {
        using var viewModel = Create(out _);

        viewModel.SelectedTab = viewModel.Tabs[1];
        Assert.Equal("スタッフ管理", Assert.IsType<AdminPlaceholder>(viewModel.Content).Title);

        viewModel.SelectedTab = viewModel.Tabs[2];
        Assert.Equal("店舗設定", Assert.IsType<AdminPlaceholder>(viewModel.Content).Title);
    }

    [Fact]
    public void 管理画面にいる間は打刻を受け付けない()
    {
        using var viewModel = Create(out var scanner);

        // 例外にならず、何も起きないこと。読み取りは無視してログにだけ残す。
        scanner.RaiseScanned("SOME-TOKEN");

        Assert.Equal("勤務状況", viewModel.SelectedTab?.Title);
    }

    [Fact]
    public void 未保存の画面からはタブを移れない()
    {
        var guard = new StubGuard { CanLeaveResult = false };
        using var viewModel = Create(out _, guard);

        var before = viewModel.SelectedTab;

        viewModel.SelectedTab = viewModel.Tabs[2];

        // 選択だけ進むと、中身と見た目がずれる。
        Assert.Same(before, viewModel.SelectedTab);
        Assert.Same(guard, viewModel.Content);
    }

    [Fact]
    public void 破棄を選べばタブを移れる()
    {
        var guard = new StubGuard { CanLeaveResult = true };
        using var viewModel = Create(out _, guard);

        viewModel.SelectedTab = viewModel.Tabs[2];

        Assert.Equal("店舗設定", viewModel.SelectedTab?.Title);
    }

    [Fact]
    public void 未保存の画面からはログアウトできない()
    {
        var guard = new StubGuard { CanLeaveResult = false };
        using var viewModel = Create(out _, guard);

        var loggedOut = false;
        viewModel.LoggedOut += (_, _) => loggedOut = true;

        viewModel.LogoutCommand.Execute(null);

        // 未保存の設定を抱えたままログアウトすると、入力が黙って消える。
        Assert.False(loggedOut);
    }

    [Fact]
    public void ログアウトを通知する()
    {
        using var viewModel = Create(out _);

        var loggedOut = false;
        viewModel.LoggedOut += (_, _) => loggedOut = true;

        viewModel.LogoutCommand.Execute(null);

        Assert.True(loggedOut);
    }

    [Fact]
    public void 破棄すると読み取りの購読を残さない()
    {
        var viewModel = Create(out var scanner);

        viewModel.Dispose();

        // 残ると、待機画面へ戻ったあとも管理画面側がログを書き続ける。
        Assert.Equal(0, scanner.ScannedSubscriberCount);
    }

    private static AdminShellViewModel Create(out CountingScanner scanner, object? firstTab = null)
    {
        scanner = new CountingScanner();

        return new AdminShellViewModel(
            scanner, new StubTabProvider(firstTab), NullLogger<AdminShellViewModel>.Instance);
    }

    /// <summary>離れてよいかを制御できる画面。</summary>
    private sealed class StubGuard : IConfirmNavigation
    {
        public bool CanLeaveResult { get; set; }

        public bool CanLeave() => CanLeaveResult;
    }

    /// <summary>
    /// 中身は仮のものにする。ここで見たいのはシェルのふるまい
    /// （切り替え・ログアウト・打刻を受け付けないこと）だけ。
    /// </summary>
    private sealed class StubTabProvider(object? firstTab = null) : IAdminTabProvider
    {
        public IReadOnlyList<AdminTab> Create() =>
        [
            new("勤務状況", () => firstTab ?? new AdminPlaceholder("勤務状況", "仮")),
            new("スタッフ管理", () => new AdminPlaceholder("スタッフ管理", "仮")),
            new("店舗設定", () => new AdminPlaceholder("店舗設定", "仮")),
        ];
    }

    /// <summary>購読の数を数えるスキャナ。止め忘れ（リーク）の検出に使う。</summary>
    private sealed class CountingScanner : IQrScannerService
    {
        private EventHandler<string>? _scanned;

        public event EventHandler<string>? Scanned
        {
            add
            {
                _scanned += value;
                ScannedSubscriberCount++;
            }
            remove
            {
                _scanned -= value;
                ScannedSubscriberCount--;
            }
        }

        public event EventHandler<ScannerState>? StateChanged;

        public event EventHandler? ScanFailed;

        public void RaiseScanFailed() => ScanFailed?.Invoke(this, EventArgs.Empty);

        public ScannerState State => ScannerState.Connected;

        public int ScannedSubscriberCount { get; private set; }

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

        public void RaiseScanned(string token) => _scanned?.Invoke(this, token);

        public void RaiseStateChanged(ScannerState state) => StateChanged?.Invoke(this, state);
    }
}
