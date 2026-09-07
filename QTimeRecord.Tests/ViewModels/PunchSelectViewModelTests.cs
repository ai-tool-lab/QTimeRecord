using Microsoft.Extensions.Logging.Abstractions;
using QTimeRecord.App.Services;
using QTimeRecord.App.ViewModels;
using QTimeRecord.Core.Devices;
using QTimeRecord.Core.Domain;
using QTimeRecord.Core.Services;

namespace QTimeRecord.Tests.ViewModels;

public sealed class PunchSelectViewModelTests
{
    [Fact]
    public async Task 対象スタッフと前回打刻を表示する()
    {
        var harness = new Harness();
        harness.Punches.Context = harness.NewContext(
            PunchState.Working, harness.NewRecord(TimeRecordType.ClockIn, new DateTime(2026, 9, 5, 21, 30, 0)));

        using var viewModel = harness.Create();
        await viewModel.LoadAsync(harness.StaffId);

        Assert.Equal("山田 太郎", viewModel.StaffName);
        Assert.Equal("E-0104", viewModel.StaffNo);
        Assert.Equal("アルバイト", viewModel.EmploymentType);
        Assert.Equal("9/5 21:30 出勤", viewModel.LastPunchText);
    }

    [Fact]
    public async Task 初回の打刻では前回欄を出さない()
    {
        var harness = new Harness();
        harness.Punches.Context = harness.NewContext(PunchState.NotClockedIn, latest: null);

        using var viewModel = harness.Create();
        await viewModel.LoadAsync(harness.StaffId);

        Assert.Null(viewModel.LastPunchText);
    }

    [Fact]
    public async Task 正常な打刻はそのまま記録して完了へ進む()
    {
        var harness = new Harness();
        harness.Punches.Outcomes.Enqueue(harness.Recorded(TimeRecordType.ClockIn));

        using var viewModel = harness.Create();
        await viewModel.LoadAsync(harness.StaffId);

        PunchOutcome? punched = null;
        viewModel.Punched += (_, o) => punched = o;

        await viewModel.PunchAsync(TimeRecordType.ClockIn);

        Assert.NotNull(punched);
        Assert.Equal(PunchStatus.Recorded, punched.Status);
        Assert.False(harness.Dialogs.ConfirmAsked);
    }

    [Fact]
    public async Task 異常な打刻は確認してから記録する()
    {
        var harness = new Harness();
        harness.Punches.Outcomes.Enqueue(harness.NeedsConfirmation("すでに出勤しています。"));
        harness.Punches.Outcomes.Enqueue(harness.Recorded(TimeRecordType.ClockIn));
        harness.Dialogs.ConfirmResult = true;

        using var viewModel = harness.Create();
        await viewModel.LoadAsync(harness.StaffId);

        PunchOutcome? punched = null;
        viewModel.Punched += (_, o) => punched = o;

        await viewModel.PunchAsync(TimeRecordType.ClockIn);

        Assert.True(harness.Dialogs.ConfirmAsked);

        // 理由を出さないと、スタッフは何を確認して押したのか分からない。
        Assert.Contains("すでに出勤しています。", harness.Dialogs.LastMessage, StringComparison.Ordinal);
        Assert.NotNull(punched);
        Assert.Equal(2, harness.Punches.Calls.Count);
        Assert.True(harness.Punches.Calls[1].WarningConfirmed);
    }

    [Fact]
    public async Task 確認をやめたら記録しない()
    {
        var harness = new Harness();
        harness.Punches.Outcomes.Enqueue(harness.NeedsConfirmation("すでに出勤しています。"));
        harness.Dialogs.ConfirmResult = false;

        using var viewModel = harness.Create();
        await viewModel.LoadAsync(harness.StaffId);

        var punched = false;
        var cancelled = false;
        viewModel.Punched += (_, _) => punched = true;
        viewModel.Cancelled += (_, _) => cancelled = true;

        await viewModel.PunchAsync(TimeRecordType.ClockIn);

        Assert.False(punched);
        Assert.Single(harness.Punches.Calls);

        // 画面には残す。やめた直後に待機へ戻ると、押し直したい人がもう一度かざす手間になる。
        Assert.False(cancelled);
    }

    [Theory]
    [InlineData(PunchStatus.Duplicate)]
    [InlineData(PunchStatus.Failed)]
    [InlineData(PunchStatus.Rejected)]
    public async Task 記録できなかった結果は完了画面へ流さない(PunchStatus status)
    {
        var harness = new Harness();
        harness.Punches.Outcomes.Enqueue(new PunchOutcome { Status = status, Message = "だめでした" });

        using var viewModel = harness.Create();
        await viewModel.LoadAsync(harness.StaffId);

        var punched = false;
        var cancelled = false;
        viewModel.Punched += (_, _) => punched = true;
        viewModel.Cancelled += (_, _) => cancelled = true;

        await viewModel.PunchAsync(TimeRecordType.ClockIn);

        // 成功の見た目で戻すと、打刻が残っていないことに誰も気づけない。
        Assert.False(punched);
        Assert.True(harness.Dialogs.ErrorShown);
        Assert.True(cancelled);
    }

    [Fact]
    public async Task 無操作30秒で中止する()
    {
        var harness = new Harness();

        using var viewModel = harness.Create();
        await viewModel.LoadAsync(harness.StaffId);

        var cancelled = false;
        viewModel.Cancelled += (_, _) => cancelled = true;

        harness.Clock.Advance(IdleTimeoutService.SelectionTimeout);
        harness.Ticker.Raise();

        // 他人の名前が出たまま残ると、次の人がその名前で打刻してしまう。
        Assert.True(cancelled);
    }

    [Fact]
    public async Task 打刻するとカウントが戻る()
    {
        var harness = new Harness();
        harness.Punches.Outcomes.Enqueue(harness.NeedsConfirmation("すでに出勤しています。"));
        harness.Dialogs.ConfirmResult = false;

        using var viewModel = harness.Create();
        await viewModel.LoadAsync(harness.StaffId);

        var cancelled = false;
        viewModel.Cancelled += (_, _) => cancelled = true;

        harness.Clock.Advance(TimeSpan.FromSeconds(29));
        harness.Ticker.Raise();

        await viewModel.PunchAsync(TimeRecordType.ClockIn);

        harness.Clock.Advance(TimeSpan.FromSeconds(29));
        harness.Ticker.Raise();

        // 確認ダイアログを読んでいる間に画面が消えてはいけない。
        Assert.False(cancelled);
        Assert.Equal(1, viewModel.RemainingSeconds);
    }

    [Fact]
    public async Task キャンセルすると時間切れは起きない()
    {
        var harness = new Harness();

        using var viewModel = harness.Create();
        await viewModel.LoadAsync(harness.StaffId);

        var cancelled = 0;
        viewModel.Cancelled += (_, _) => cancelled++;

        viewModel.CancelCommand.Execute(null);

        harness.Clock.Advance(IdleTimeoutService.SelectionTimeout);
        harness.Ticker.Raise();

        Assert.Equal(1, cancelled);
    }

    [Fact]
    public async Task 破棄するとタイマーが残らない()
    {
        var harness = new Harness();

        var viewModel = harness.Create();
        await viewModel.LoadAsync(harness.StaffId);

        viewModel.Dispose();

        // 止め忘れたタイマーは、次の画面を勝手に巻き戻す。
        Assert.Equal(0, harness.Ticker.SubscriberCount);
    }

    [Fact]
    public async Task リーダーが切れたら打刻できずキャンセルだけ残る()
    {
        var harness = new Harness();

        using var viewModel = harness.Create();
        await viewModel.LoadAsync(harness.StaffId);

        Assert.True(viewModel.CanPunch);
        Assert.True(viewModel.PunchCommand.CanExecute(TimeRecordType.ClockIn));
        Assert.Null(viewModel.ScannerWarning);

        harness.Scanner.RaiseStateChanged(ScannerState.Disconnected);

        Assert.False(viewModel.CanPunch);
        Assert.False(viewModel.PunchCommand.CanExecute(TimeRecordType.ClockIn));
        Assert.NotNull(viewModel.ScannerWarning);
        Assert.True(viewModel.CancelCommand.CanExecute(null));
    }

    [Fact]
    public async Task 対象を読み込めなければ待機へ戻す()
    {
        var harness = new Harness();
        harness.Punches.Context = null;

        using var viewModel = harness.Create();

        var cancelled = false;
        viewModel.Cancelled += (_, _) => cancelled = true;

        await viewModel.LoadAsync(harness.StaffId);

        Assert.True(cancelled);
    }

    // ---- 補助 ----

    private sealed class Harness
    {
        public Harness()
        {
            StaffId = Guid.CreateVersion7();

            Staff = new Staff
            {
                Id = StaffId,
                StoreId = Guid.CreateVersion7(),
                StaffNo = "E-0104",
                Name = "山田 太郎",
                EmploymentType = "アルバイト",
                Status = StaffStatus.Active,
            };

            Punches = new StubPunchService { Context = NewContext(PunchState.NotClockedIn, latest: null) };
        }

        public Guid StaffId { get; }

        public Staff Staff { get; }

        public StubPunchService Punches { get; }

        public StubDialogService Dialogs { get; } = new();

        public StubScanner Scanner { get; } = new();

        public StubTicker Ticker { get; } = new();

        public TestClock Clock { get; } = new();

        public PunchSelectViewModel Create() => new(
            Punches,
            Scanner,
            Dialogs,
            new IdleTimeoutService(Clock, Ticker),
            NullLogger<PunchSelectViewModel>.Instance);

        public PunchContext NewContext(PunchState state, TimeRecord? latest)
            => new(Staff, state, new DateOnly(2026, 9, 6), latest);

        public TimeRecord NewRecord(TimeRecordType type, DateTime recordedAt) => new()
        {
            Id = Guid.CreateVersion7(),
            StoreId = Staff.StoreId,
            StaffId = StaffId,
            RecordType = type,
            RecordedAt = recordedAt,
            WorkDate = DateOnly.FromDateTime(recordedAt),
            EntryMethod = EntryMethod.Qr,
        };

        public PunchOutcome Recorded(TimeRecordType type) => new()
        {
            Status = PunchStatus.Recorded,
            Message = $"{PunchService.Describe(type)}を記録しました",
            Record = NewRecord(type, Clock.Now),
        };

        public PunchOutcome NeedsConfirmation(string reason)
            => new() { Status = PunchStatus.NeedsConfirmation, Message = reason };
    }

    private sealed class StubPunchService : IPunchService
    {
        public PunchContext? Context { get; set; }

        public Queue<PunchOutcome> Outcomes { get; } = new();

        public List<PunchRequest> Calls { get; } = [];

        public Task<PunchContext?> GetContextAsync(Guid staffId, CancellationToken ct = default)
            => Task.FromResult(Context);

        public Task<PunchOutcome> PunchAsync(PunchRequest request, CancellationToken ct = default)
        {
            Calls.Add(request);

            return Task.FromResult(Outcomes.Count > 0
                ? Outcomes.Dequeue()
                : new PunchOutcome { Status = PunchStatus.Recorded, Message = "記録しました" });
        }
    }

    private sealed class StubDialogService : IDialogService
    {
        public bool ConfirmResult { get; set; }

        public bool ConfirmAsked { get; private set; }

        public bool ErrorShown { get; private set; }

        public string LastMessage { get; private set; } = string.Empty;

        public bool Confirm(
            string title, string message, string okText = "OK", string cancelText = "キャンセル")
        {
            ConfirmAsked = true;
            LastMessage = message;
            return ConfirmResult;
        }

        public void ShowInfo(string title, string message) => LastMessage = message;

        public void ShowError(string title, string message)
        {
            ErrorShown = true;
            LastMessage = message;
        }
    }

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
}
