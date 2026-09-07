using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using QTimeRecord.Core.Devices;

namespace QTimeRecord.Tests.Devices;

/// <summary>
/// 接続の維持と再接続。店舗では USB を抜かれることが普通に起きるため、
/// 切断からの復帰を実機なしで確認しておく。
/// </summary>
public sealed class QrScannerServiceTests
{
    private static readonly SerialPortSettings Settings = new() { PortName = "COM3" };

    private sealed class StubFactory : ISerialPortFactory
    {
        private readonly Queue<FakeSerialPort> _queued = new();

        public List<FakeSerialPort> Created { get; } = [];

        public FakeSerialPort Latest => Created[^1];

        /// <summary>次に作られるポートの振る舞いを指定する。</summary>
        public void Enqueue(FakeSerialPort port) => _queued.Enqueue(port);

        public ISerialPort Create()
        {
            var port = _queued.Count > 0 ? _queued.Dequeue() : new FakeSerialPort();
            Created.Add(port);

            return port;
        }
    }

    private sealed class StubEnumerator : ISerialPortEnumerator
    {
        public List<string> Ports { get; } = ["COM3"];

        public IReadOnlyList<string> GetPortNames() => Ports;
    }

    private static (QrScannerService Service, StubFactory Factory, StubEnumerator Enumerator, TestClock Clock)
        Create()
    {
        var factory = new StubFactory();
        var enumerator = new StubEnumerator();
        var clock = new TestClock();

        var service = new QrScannerService(
            factory, enumerator, clock, NullLogger<QrScannerService>.Instance);

        return (service, factory, enumerator, clock);
    }

    // ---- 接続と読み取り ----

    [Fact]
    public void 開始すると接続状態になる()
    {
        var (service, _, _, _) = Create();

        service.Start(Settings);

        Assert.Equal(ScannerState.Connected, service.State);
    }

    [Fact]
    public void 読み取ったトークンが通知される()
    {
        var (service, factory, _, _) = Create();
        var scanned = new List<string>();
        service.Scanned += (_, token) => scanned.Add(token);

        service.Start(Settings);
        factory.Latest.Receive("TOKEN123\r\n");

        Assert.Equal(["TOKEN123"], scanned);
    }

    [Fact]
    public void ポート未設定なら接続を試みない()
    {
        var (service, factory, _, _) = Create();

        service.Start(new SerialPortSettings { PortName = "" });

        Assert.Equal(ScannerState.Disconnected, service.State);
        Assert.Empty(factory.Created);
    }

    // ---- 切断と再接続 ----

    [Fact]
    public void エラーを受けると切断状態になる()
    {
        var (service, factory, _, _) = Create();
        service.Start(Settings);

        factory.Latest.RaiseError();

        Assert.Equal(ScannerState.Disconnected, service.State);
    }

    [Fact]
    public void 待ち時間が経つと再接続する()
    {
        var (service, factory, _, clock) = Create();
        service.Start(Settings);
        factory.Latest.RaiseError();

        // 1秒経つまでは試みない
        clock.AdvanceSeconds(0.9);
        service.Tick();
        Assert.Equal(ScannerState.Disconnected, service.State);

        clock.AdvanceSeconds(0.2);
        service.Tick();

        Assert.Equal(ScannerState.Connected, service.State);
    }

    [Fact]
    public void 再接続後も読み取りが再開する()
    {
        var (service, factory, _, clock) = Create();
        var scanned = new List<string>();
        service.Scanned += (_, token) => scanned.Add(token);

        service.Start(Settings);
        factory.Latest.RaiseError();

        clock.AdvanceSeconds(2);
        service.Tick();
        factory.Latest.Receive("AFTER-RECONNECT\r\n");

        Assert.Equal(["AFTER-RECONNECT"], scanned);
    }

    [Fact]
    public void USBを抜かれるとポート一覧から検知する()
    {
        var (service, _, enumerator, clock) = Create();
        service.Start(Settings);

        // 機種によっては例外もイベントも上がらない。一覧から消えたことで気づく。
        enumerator.Ports.Clear();

        clock.AdvanceSeconds(5);
        service.Tick();

        Assert.Equal(ScannerState.Disconnected, service.State);
    }

    [Fact]
    public void 接続中は不要にポートを開き直さない()
    {
        var (service, factory, _, clock) = Create();
        service.Start(Settings);

        for (var i = 0; i < 10; i++)
        {
            clock.AdvanceSeconds(1);
            service.Tick();
        }

        Assert.Single(factory.Created);
    }

    [Fact]
    public void 開けないあいだは待ち時間を延ばして試み続ける()
    {
        var (service, factory, _, clock) = Create();

        for (var i = 0; i < 6; i++)
        {
            var failing = new FakeSerialPort { OpenFailure = new IOException("開けません") };
            factory.Enqueue(failing);
        }

        service.Start(Settings);
        Assert.Equal(ScannerState.Disconnected, service.State);

        // 1s → 2s → 5s → 10s → 30s と延びる
        foreach (var delay in new[] { 1, 2, 5, 10, 30 })
        {
            clock.AdvanceSeconds(delay - 0.1);
            service.Tick();

            clock.AdvanceSeconds(0.2);
            service.Tick();
        }

        Assert.Equal(6, factory.Created.Count);
        Assert.Equal(ScannerState.Disconnected, service.State);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 5)]
    [InlineData(3, 10)]
    [InlineData(4, 30)]
    [InlineData(99, 30)] // 頭打ち。延び続けると復帰に何分もかかる
    public void 再接続の待ち時間(int attempt, int expectedSeconds)
    {
        Assert.Equal(
            TimeSpan.FromSeconds(expectedSeconds), QrScannerService.GetReconnectDelay(attempt));
    }

    // ---- 状態の通知 ----

    [Fact]
    public void 状態が変わったときだけ通知される()
    {
        var (service, factory, _, _) = Create();
        var states = new List<ScannerState>();
        service.StateChanged += (_, state) => states.Add(state);

        service.Start(Settings);
        factory.Latest.RaiseError();

        Assert.Equal([ScannerState.Connected, ScannerState.Disconnected], states);
    }

    [Fact]
    public void 停止すると読み取りを受け付けない()
    {
        var (service, factory, _, _) = Create();
        var scanned = new List<string>();
        service.Scanned += (_, token) => scanned.Add(token);

        service.Start(Settings);
        var port = factory.Latest;
        service.Stop();

        port.Receive("IGNORED\r\n");

        Assert.Equal(ScannerState.Stopped, service.State);
        Assert.Empty(scanned);
    }

    // ---- 接続テスト ----

    [Fact]
    public async Task 接続テストは読み取りがあれば成功する()
    {
        var (service, factory, _, _) = Create();

        var testing = service.TestConnectionAsync(Settings, TimeSpan.FromSeconds(5));

        // ポートが開かれるまで待つ
        while (factory.Created.Count == 0)
        {
            await Task.Yield();
        }

        factory.Latest.Receive("TEST-TOKEN\r\n");

        var result = await testing;

        Assert.True(result.Success);
        Assert.Equal("TEST-TOKEN", result.Token);
    }

    [Fact]
    public async Task 読み取りが無ければ失敗として案内する()
    {
        var (service, _, _, _) = Create();

        var result = await service.TestConnectionAsync(Settings, TimeSpan.FromMilliseconds(50));

        Assert.False(result.Success);
        Assert.Contains("読み取りがありません", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 使用中のポートは理由を添えて失敗する()
    {
        var (service, factory, _, _) = Create();
        factory.Enqueue(new FakeSerialPort { OpenFailure = new UnauthorizedAccessException() });

        var result = await service.TestConnectionAsync(Settings, TimeSpan.FromMilliseconds(50));

        Assert.False(result.Success);
        Assert.Contains("使用中", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ポート未選択ならテストしない()
    {
        var (service, factory, _, _) = Create();

        var result = await service.TestConnectionAsync(
            new SerialPortSettings { PortName = "" }, TimeSpan.FromSeconds(1));

        Assert.False(result.Success);
        Assert.Empty(factory.Created);
    }

    [Fact]
    public async Task テスト後は元の受信状態へ戻る()
    {
        var (service, factory, _, _) = Create();
        service.Start(Settings);

        await service.TestConnectionAsync(Settings, TimeSpan.FromMilliseconds(50));

        // 戻し忘れると、テストしただけで打刻を受け付けなくなる
        Assert.Equal(ScannerState.Connected, service.State);

        var scanned = new List<string>();
        service.Scanned += (_, token) => scanned.Add(token);
        factory.Latest.Receive("AFTER-TEST\r\n");

        Assert.Equal(["AFTER-TEST"], scanned);
    }
}
