using Microsoft.Extensions.Logging.Abstractions;
using QTimeRecord.App.Services;
using QTimeRecord.App.ViewModels;
using QTimeRecord.Core.Data.Repositories;
using QTimeRecord.Core.Devices;
using QTimeRecord.Core.Domain;
using QTimeRecord.Core.Services;

namespace QTimeRecord.Tests.ViewModels;

public sealed class StoreSettingsViewModelTests
{
    [Fact]
    public async Task 現在の設定を読み込む()
    {
        var harness = new Harness();

        var viewModel = harness.Create();
        await viewModel.LoadAsync();

        Assert.Equal("テスト株式会社", viewModel.CompanyName);
        Assert.Equal("相模原店", viewModel.StoreName);
        Assert.Equal("09:00", viewModel.BusinessDayStart);
        Assert.Equal("22:00", viewModel.BusinessDayEnd);
        Assert.Equal("COM3", viewModel.ComPort);

        // 読み込んだ直後は未保存ではない。
        Assert.False(viewModel.IsDirty);
    }

    [Fact]
    public async Task 入力すると未保存になる()
    {
        var harness = new Harness();

        var viewModel = harness.Create();
        await viewModel.LoadAsync();

        viewModel.StoreName = "町田店";

        Assert.True(viewModel.IsDirty);
        Assert.True(viewModel.SaveCommand.CanExecute(null));
    }

    [Fact]
    public async Task お知らせの本文を直しても未保存になる()
    {
        var harness = new Harness();
        harness.Settings.Announcements.Add(new Announcement
        {
            Id = Guid.CreateVersion7(),
            StoreId = Guid.CreateVersion7(),
            DisplayOrder = 1,
            Heading = "健康診断",
            Body = "受診希望日を月末までに提出願います。",
        });

        var viewModel = harness.Create();
        await viewModel.LoadAsync();

        Assert.False(viewModel.IsDirty);

        viewModel.Announcements[0].Body = "書き換えた本文";

        // 中身の変更を拾えないと、直したつもりの本文が保存されない。
        Assert.True(viewModel.IsDirty);
    }

    [Fact]
    public async Task 接続テストの結果表示では未保存にならない()
    {
        var harness = new Harness();

        var viewModel = harness.Create();
        await viewModel.LoadAsync();

        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)viewModel.TestConnectionCommand)
            .ExecuteAsync(null);

        // 状態の表示は「変更」ではない。含めると常に未保存になる。
        Assert.NotNull(viewModel.TestResult);
        Assert.False(viewModel.IsDirty);
    }

    [Fact]
    public async Task お知らせは五件まで追加できる()
    {
        var harness = new Harness();

        var viewModel = harness.Create();
        await viewModel.LoadAsync();

        for (var i = 0; i < 5; i++)
        {
            viewModel.AddAnnouncementCommand.Execute(null);
        }

        Assert.Equal(5, viewModel.Announcements.Count);
        Assert.Equal("5 / 5 件", viewModel.AnnouncementCountText);

        // 6件目は押せないようにする。押せてから弾くのは分かりにくい。
        Assert.False(viewModel.CanAddAnnouncement);
        Assert.False(viewModel.AddAnnouncementCommand.CanExecute(null));
    }

    [Fact]
    public async Task お知らせを削除できる()
    {
        var harness = new Harness();

        var viewModel = harness.Create();
        await viewModel.LoadAsync();

        viewModel.AddAnnouncementCommand.Execute(null);
        var item = viewModel.Announcements[0];

        viewModel.RemoveAnnouncementCommand.Execute(item);

        Assert.Empty(viewModel.Announcements);
    }

    [Theory]
    [InlineData("9:00")]
    [InlineData("24:00")]
    [InlineData("あさ")]
    [InlineData("")]
    public async Task 時刻の書式が不正なら保存しない(string text)
    {
        var harness = new Harness();

        var viewModel = harness.Create();
        await viewModel.LoadAsync();

        viewModel.BusinessDayStart = text;

        await viewModel.SaveAsync();

        Assert.NotNull(viewModel.ErrorMessage);
        Assert.Empty(harness.Settings.Saved);
    }

    [Fact]
    public async Task 保存すると設定が渡される()
    {
        var harness = new Harness();

        var viewModel = harness.Create();
        await viewModel.LoadAsync();

        viewModel.StoreName = "町田店";
        viewModel.BusinessDayStart = "11:00";
        viewModel.BusinessDayEnd = "05:00";

        await viewModel.SaveAsync();

        var draft = Assert.Single(harness.Settings.Saved);
        Assert.Equal("町田店", draft.StoreName);
        Assert.Equal(new TimeOnly(11, 0), draft.BusinessDayStart);
        Assert.Equal(new TimeOnly(5, 0), draft.BusinessDayEnd);
        Assert.False(viewModel.IsDirty);
    }

    [Fact]
    public async Task 保存するとリーダーを繋ぎ直す()
    {
        var harness = new Harness();

        var viewModel = harness.Create();
        await viewModel.LoadAsync();

        viewModel.ComPort = "COM5";
        await viewModel.SaveAsync();

        // 保存しただけでは受信中の設定は変わらない。繋ぎ直して初めて反映される。
        Assert.Equal(1, harness.Devices.RestartCalls);
    }

    [Fact]
    public async Task 保存に失敗したら入力を残す()
    {
        var harness = new Harness();
        harness.Settings.ThrowOnSave = true;

        var viewModel = harness.Create();
        await viewModel.LoadAsync();

        viewModel.StoreName = "町田店";
        await viewModel.SaveAsync();

        // 消してしまうと、入力し直しになる。
        Assert.Equal("町田店", viewModel.StoreName);
        Assert.True(viewModel.IsDirty);
        Assert.NotNull(viewModel.ErrorMessage);
    }

    [Fact]
    public async Task 未保存のまま離れるときは確認する()
    {
        var harness = new Harness();
        harness.Dialogs.ConfirmResult = false;

        var viewModel = harness.Create();
        await viewModel.LoadAsync();

        Assert.True(viewModel.CanLeave());

        viewModel.StoreName = "町田店";

        // 保存し忘れは「設定したのに反映されない」形で現れ、原因が分かりにくい。
        Assert.False(viewModel.CanLeave());
        Assert.True(harness.Dialogs.ConfirmAsked);
    }

    [Fact]
    public async Task 破棄を選べば離れられる()
    {
        var harness = new Harness();
        harness.Dialogs.ConfirmResult = true;

        var viewModel = harness.Create();
        await viewModel.LoadAsync();

        viewModel.StoreName = "町田店";

        Assert.True(viewModel.CanLeave());
    }

    [Fact]
    public async Task ポートを再検出しても保存済みの選択は残る()
    {
        var harness = new Harness();
        harness.Ports.Names = ["COM1", "COM4"];

        var viewModel = harness.Create();
        await viewModel.LoadAsync();

        // COM3 は今つながっていないが、消すと設定を開いただけで値が失われる。
        Assert.Equal("COM3", viewModel.ComPort);
        Assert.Contains("COM3", viewModel.AvailablePorts);
    }

    [Fact]
    public async Task ポートが未選択なら接続テストしない()
    {
        var harness = new Harness();
        harness.Settings.ComPort = null;

        var viewModel = harness.Create();
        await viewModel.LoadAsync();

        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)viewModel.TestConnectionCommand)
            .ExecuteAsync(null);

        Assert.False(viewModel.TestSucceeded);
        Assert.Equal(0, harness.Scanner.TestCalls);
    }

    [Fact]
    public async Task 接続テストの成否を表示する()
    {
        var harness = new Harness();
        harness.Scanner.TestResult = new ConnectionTestResult(true, "読み取りを確認しました。");

        var viewModel = harness.Create();
        await viewModel.LoadAsync();

        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)viewModel.TestConnectionCommand)
            .ExecuteAsync(null);

        Assert.True(viewModel.TestSucceeded);
        Assert.Equal("読み取りを確認しました。", viewModel.TestResult);
    }

    [Fact]
    public async Task 接続テストの設定は画面の入力を使う()
    {
        var harness = new Harness();

        var viewModel = harness.Create();
        await viewModel.LoadAsync();

        viewModel.BaudRate = 19200;
        viewModel.Parity = "Even";
        viewModel.StopBits = "Two";

        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)viewModel.TestConnectionCommand)
            .ExecuteAsync(null);

        // 保存済みの設定で試すと、直した内容を確かめられない。
        Assert.NotNull(harness.Scanner.LastSettings);
        Assert.Equal(19200, harness.Scanner.LastSettings.BaudRate);
        Assert.Equal(System.IO.Ports.Parity.Even, harness.Scanner.LastSettings.Parity);
        Assert.Equal(System.IO.Ports.StopBits.Two, harness.Scanner.LastSettings.StopBits);
    }

    [Fact]
    public async Task 接続テストが失敗しても理由を出す()
    {
        var harness = new Harness();
        harness.Scanner.TestResult = new ConnectionTestResult(false, "ポートを開けませんでした。");

        var viewModel = harness.Create();
        await viewModel.LoadAsync();

        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)viewModel.TestConnectionCommand)
            .ExecuteAsync(null);

        Assert.False(viewModel.TestSucceeded);
        Assert.Equal("ポートを開けませんでした。", viewModel.TestResult);
    }

    // ---- 補助 ----

    private sealed class Harness
    {
        public StubSettingsService Settings { get; } = new();

        public StubScanner Scanner { get; } = new();

        public StubEnumerator Ports { get; } = new();

        public StubDeviceRepository Devices { get; } = new();

        public StubDialogs Dialogs { get; } = new();

        public StoreSettingsViewModel Create() => new(
            Settings,
            Scanner,
            Ports,
            new ScannerHost(Scanner, Devices, new StubTicker(), NullLogger<ScannerHost>.Instance),
            Dialogs,
            NullLogger<StoreSettingsViewModel>.Instance);
    }

    private sealed class StubSettingsService : IStoreSettingsService
    {
        public List<Announcement> Announcements { get; } = [];

        public List<StoreSettingsDraft> Saved { get; } = [];

        public string? ComPort { get; set; } = "COM3";

        public bool ThrowOnSave { get; set; }

        public Task<StoreSettingsSnapshot?> GetAsync(CancellationToken ct = default)
        {
            var store = new Store
            {
                Id = Guid.CreateVersion7(),
                CompanyName = "テスト株式会社",
                StoreName = "相模原店",
                StoreCode = "101",
                BusinessDayStart = new TimeOnly(9, 0),
                BusinessDayEnd = new TimeOnly(22, 0),
            };

            return Task.FromResult<StoreSettingsSnapshot?>(new StoreSettingsSnapshot(
                store,
                Announcements,
                new DeviceSettings { StoreId = store.Id, ComPort = ComPort }));
        }

        public string? Validate(StoreSettingsDraft draft) => null;

        public Task SaveAsync(StoreSettingsDraft draft, CancellationToken ct = default)
        {
            if (ThrowOnSave)
            {
                throw new InvalidOperationException("保存に失敗しました。");
            }

            Saved.Add(draft);

            return Task.CompletedTask;
        }
    }

    private sealed class StubScanner : IQrScannerService
    {
        public ScannerState State => ScannerState.Connected;

        public ConnectionTestResult TestResult { get; set; } = new(true, "ok");

        public SerialPortSettings? LastSettings { get; private set; }

        public int TestCalls { get; private set; }

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
        {
            LastSettings = settings;
            TestCalls++;

            return Task.FromResult(TestResult);
        }

        public void Dispose()
        {
        }

        public void Raise()
        {
            Scanned?.Invoke(this, "TOKEN");
            StateChanged?.Invoke(this, ScannerState.Connected);
        }
    }

    private sealed class StubEnumerator : ISerialPortEnumerator
    {
        public IReadOnlyList<string> Names { get; set; } = ["COM1", "COM3"];

        public IReadOnlyList<string> GetPortNames() => Names;
    }

    private sealed class StubDeviceRepository : IDeviceSettingsRepository
    {
        public int RestartCalls { get; private set; }

        public Task<DeviceSettings?> GetAsync(Guid storeId, CancellationToken ct = default)
        {
            RestartCalls++;

            return Task.FromResult<DeviceSettings?>(
                new DeviceSettings { StoreId = storeId, ComPort = "COM3" });
        }

        public Task SaveAsync(DeviceSettings settings, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class StubDialogs : IDialogService
    {
        public bool ConfirmResult { get; set; }

        public bool ConfirmAsked { get; private set; }

        public bool Confirm(
            string title, string message, string okText = "OK", string cancelText = "キャンセル")
        {
            ConfirmAsked = true;
            return ConfirmResult;
        }

        public void ShowInfo(string title, string message)
        {
        }

        public void ShowError(string title, string message)
        {
        }
    }
}
