using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using QTimeRecord.App.Services;
using QTimeRecord.Core.Devices;
using QTimeRecord.Core.Domain;
using QTimeRecord.Core.Services;

namespace QTimeRecord.App.ViewModels;

/// <summary>
/// 打刻種別の選択画面。
///
/// QR で本人を特定したあと、出勤／退勤／中抜け開始／中抜け終了 を選ばせる。
/// <b>無操作で自動的に待機画面へ戻す。</b>他人の名前が出たまま残ると、
/// 次の人がその名前で打刻してしまう。
/// </summary>
public sealed partial class PunchSelectViewModel : ObservableObject, IDisposable
{
    private readonly IPunchService _punches;
    private readonly IQrScannerService _scanner;
    private readonly IDialogService _dialogs;
    private readonly IdleTimeoutService _timeout;
    private readonly ILogger<PunchSelectViewModel> _logger;

    private Guid _staffId;
    private bool _disposed;

    public PunchSelectViewModel(
        IPunchService punches,
        IQrScannerService scanner,
        IDialogService dialogs,
        IdleTimeoutService timeout,
        ILogger<PunchSelectViewModel> logger)
    {
        _punches = punches;
        _scanner = scanner;
        _dialogs = dialogs;
        _timeout = timeout;
        _logger = logger;

        _scannerState = scanner.State;

        PunchCommand = new AsyncRelayCommand<TimeRecordType?>(PunchAsync, _ => CanPunch);
        CancelCommand = new RelayCommand(Cancel);

        _scanner.StateChanged += HandleScannerStateChanged;
        _timeout.Elapsed += OnTimeoutElapsed;
        _timeout.Remaining += OnRemainingChanged;
    }

    /// <summary>打刻が確定した。呼び出し側が完了画面へ進める。</summary>
    public event EventHandler<PunchOutcome>? Punched;

    /// <summary>キャンセルまたは時間切れ。呼び出し側が待機画面へ戻す。</summary>
    public event EventHandler? Cancelled;

    [ObservableProperty]
    private string _staffName = string.Empty;

    [ObservableProperty]
    private string? _staffNo;

    [ObservableProperty]
    private string? _employmentType;

    /// <summary>直近の打刻。誤打刻に気づけるよう出す。無ければ null。</summary>
    [ObservableProperty]
    private string? _lastPunchText;

    [ObservableProperty]
    private int _remainingSeconds;

    [ObservableProperty]
    private ScannerState _scannerState;

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>自動キャンセルまでの秒数。残り時間バーの上限に使う。</summary>
    public int TimeoutSeconds => (int)IdleTimeoutService.SelectionTimeout.TotalSeconds;

    /// <summary>
    /// 打刻ボタンを押せるか。
    ///
    /// リーダーが切れている端末は、この人の打刻を終えても次の人を受け付けられない。
    /// 半端に使える状態にせず、キャンセルだけ残して復旧を促す（→ plan.md 9-2）。
    /// </summary>
    public bool CanPunch => ScannerState == ScannerState.Connected && !IsBusy;

    public string? ScannerWarning => ScannerState == ScannerState.Connected
        ? null
        : "QRリーダーが接続されていません。ケーブルを確認してください。";

    /// <summary>打刻する。XAML からは種別を CommandParameter で渡す。</summary>
    public IAsyncRelayCommand<TimeRecordType?> PunchCommand { get; }

    public ICommand CancelCommand { get; }

    /// <summary>対象スタッフを読み込んでカウントダウンを始める。</summary>
    public async Task LoadAsync(Guid staffId, CancellationToken ct = default)
    {
        _staffId = staffId;

        var context = await _punches.GetContextAsync(staffId, ct);

        if (context is null)
        {
            _logger.LogWarning("打刻の対象を読み込めませんでした: {StaffId}", staffId);
            Cancelled?.Invoke(this, EventArgs.Empty);
            return;
        }

        StaffName = context.Staff.Name;
        StaffNo = context.Staff.StaffNo;
        EmploymentType = context.Staff.EmploymentType;
        LastPunchText = DescribeLastPunch(context.LatestRecord);

        _timeout.Start(IdleTimeoutService.SelectionTimeout);
        RemainingSeconds = _timeout.RemainingSeconds;
    }

    /// <summary>直近の打刻の表示。「前回: 09/05 21:30 退勤」。</summary>
    public static string? DescribeLastPunch(TimeRecord? record) => record is null
        ? null
        : $"{record.RecordedAt:M/d HH:mm} {PunchService.Describe(record.RecordType)}";

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _scanner.StateChanged -= HandleScannerStateChanged;
        _timeout.Elapsed -= OnTimeoutElapsed;
        _timeout.Remaining -= OnRemainingChanged;
        _timeout.Dispose();
    }

    /// <summary>打刻する。テストから直接呼べるよう公開する。</summary>
    public async Task PunchAsync(TimeRecordType? type)
    {
        if (type is null || IsBusy)
        {
            return;
        }

        // 押した時点で無操作ではない。時間切れで画面が消えないよう起点を戻す。
        _timeout.Reset();
        RemainingSeconds = _timeout.RemainingSeconds;

        IsBusy = true;

        try
        {
            var outcome = await _punches.PunchAsync(NewRequest(type.Value));

            if (outcome.Status == PunchStatus.NeedsConfirmation)
            {
                if (!ConfirmWarning(type.Value, outcome.Message))
                {
                    return;
                }

                outcome = await _punches.PunchAsync(NewRequest(type.Value, warningConfirmed: true));
            }

            Complete(outcome);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Complete(PunchOutcome outcome)
    {
        // 記録できていない結果を完了画面へ流さない。
        // 成功の見た目で戻すと、打刻が消えたことに誰も気づけない。
        if (!outcome.IsRecorded)
        {
            _dialogs.ShowError("打刻できませんでした", outcome.Message);
            Cancelled?.Invoke(this, EventArgs.Empty);
            return;
        }

        _timeout.Stop();
        Punched?.Invoke(this, outcome);
    }

    private bool ConfirmWarning(TimeRecordType type, string reason)
    {
        var label = PunchService.Describe(type);

        _logger.LogInformation("異常な打刻の確認を求めます: {StaffId} {RecordType}", _staffId, type);

        var confirmed = _dialogs.Confirm(
            $"{label}を記録しますか？",
            $"{reason}{Environment.NewLine}このまま記録すると、管理者の確認対象になります。",
            $"{label}を記録する",
            "やめる");

        // ダイアログを読んでいる間に時間切れにならないよう、閉じた時点で数え直す。
        _timeout.Reset();
        RemainingSeconds = _timeout.RemainingSeconds;

        return confirmed;
    }

    private PunchRequest NewRequest(TimeRecordType type, bool warningConfirmed = false) => new()
    {
        StaffId = _staffId,
        RecordType = type,
        WarningConfirmed = warningConfirmed,
    };

    private void Cancel()
    {
        _timeout.Stop();
        Cancelled?.Invoke(this, EventArgs.Empty);
    }

    private void OnTimeoutElapsed(object? sender, EventArgs e)
    {
        _logger.LogInformation("無操作のため打刻を中止しました: {StaffId}", _staffId);
        Cancelled?.Invoke(this, EventArgs.Empty);
    }

    private void OnRemainingChanged(object? sender, TimeSpan remaining)
        => RemainingSeconds = _timeout.RemainingSeconds;

    private void HandleScannerStateChanged(object? sender, ScannerState state)
        => ScannerState = state;

    partial void OnScannerStateChanged(ScannerState value)
    {
        OnPropertyChanged(nameof(ScannerWarning));
        NotifyCanPunchChanged();
    }

    partial void OnIsBusyChanged(bool value) => NotifyCanPunchChanged();

    private void NotifyCanPunchChanged()
    {
        OnPropertyChanged(nameof(CanPunch));

        // ボタンの有効・無効は CanExecute で決まる。通知しないと押せたままになる。
        PunchCommand.NotifyCanExecuteChanged();
    }
}
