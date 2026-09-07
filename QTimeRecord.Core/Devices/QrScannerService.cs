using Microsoft.Extensions.Logging;
using QTimeRecord.Core.Infrastructure;

namespace QTimeRecord.Core.Devices;

public enum ScannerState
{
    /// <summary>使っていない。</summary>
    Stopped,

    /// <summary>接続済み。読み取りを待っている。</summary>
    Connected,

    /// <summary>接続できていない。再接続を試みている。</summary>
    Disconnected,
}

public sealed record ConnectionTestResult(bool Success, string Message, string? Token = null);

public interface IQrScannerService : IDisposable
{
    ScannerState State { get; }

    /// <summary>QR を1件読み取った。</summary>
    event EventHandler<string>? Scanned;

    /// <summary>
    /// 受信はあったが、読み取れる形ではなかった。
    ///
    /// 何も起きないと、かざした人は端末が壊れていると受け取る。
    /// 内容は渡さない（→ plan.md 15-1）。
    /// </summary>
    event EventHandler? ScanFailed;

    event EventHandler<ScannerState>? StateChanged;

    /// <summary>受信を開始する。失敗しても例外にせず、再接続を続ける。</summary>
    void Start(SerialPortSettings settings);

    void Stop();

    /// <summary>
    /// 監視・再接続・終端なしの確定を1回分進める。
    /// 内部にタイマーを持たず外から刻ませるのは、実時間を待たずにテストするため。
    /// </summary>
    void Tick();

    /// <summary>接続テスト。実行中の受信は一時的に止め、終了後に元へ戻す。</summary>
    Task<ConnectionTestResult> TestConnectionAsync(
        SerialPortSettings settings, TimeSpan timeout, CancellationToken ct = default);
}

/// <summary>
/// QR リーダーとの接続を保つ。
///
/// 店舗では USB を抜かれる・電源が一瞬落ちる・別アプリがポートを掴む、が普通に起きる。
/// <b>切れたら諦めるのではなく、状態を画面に出しながら再接続を試み続ける。</b>
/// </summary>
public sealed class QrScannerService : IQrScannerService
{
    /// <summary>再接続の待ち時間。刻んで長くし、最後は30秒で頭打ちにする。</summary>
    private static readonly TimeSpan[] ReconnectDelays =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30),
    ];

    /// <summary>ポートの存在確認の間隔。USB の抜去はイベントで拾えないことがある。</summary>
    private static readonly TimeSpan PortCheckInterval = TimeSpan.FromSeconds(5);

    private readonly ISerialPortFactory _factory;
    private readonly ISerialPortEnumerator _enumerator;
    private readonly IClock _clock;
    private readonly ILogger<QrScannerService> _logger;
    private readonly ScanBuffer _buffer = new();
    private readonly Lock _gate = new();

    private ISerialPort? _port;
    private SerialPortSettings? _settings;
    private ScannerState _state = ScannerState.Stopped;
    private int _reconnectAttempt;
    private DateTime _nextReconnectAt;
    private DateTime _lastPortCheckAt;

    public QrScannerService(
        ISerialPortFactory factory,
        ISerialPortEnumerator enumerator,
        IClock clock,
        ILogger<QrScannerService> logger)
    {
        _factory = factory;
        _enumerator = enumerator;
        _clock = clock;
        _logger = logger;

        // 長すぎる受信は捨てられる。捨てたこと自体が残らないと、
        // 「かざしても反応しない」の原因を追えない（→ plan.md 15-1）。
        _buffer.Overflowed += (_, length) =>
        {
            _logger.LogWarning("受信が長すぎるため破棄しました。{Length} 文字", length);
            ScanFailed?.Invoke(this, EventArgs.Empty);
        };
    }

    public ScannerState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    public event EventHandler<string>? Scanned;

    public event EventHandler? ScanFailed;

    public event EventHandler<ScannerState>? StateChanged;

    /// <summary>再接続の待ち時間。0 回目から順に 1s / 2s / 5s / 10s / 30s、以降は 30s。</summary>
    public static TimeSpan GetReconnectDelay(int attempt)
        => ReconnectDelays[Math.Clamp(attempt, 0, ReconnectDelays.Length - 1)];

    public void Start(SerialPortSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        Stop();

        _settings = settings;
        _reconnectAttempt = 0;
        _lastPortCheckAt = _clock.Now;

        if (!settings.HasPort)
        {
            _logger.LogWarning("COM ポートが設定されていないため、接続を開始しません。");
            SetState(ScannerState.Disconnected);
            return;
        }

        TryOpen();
    }

    public void Stop()
    {
        ClosePort();
        _settings = null;
        _buffer.Reset();
        SetState(ScannerState.Stopped);
    }

    /// <summary>
    /// 監視・再接続・終端なしの確定を1回分進める。
    /// アプリ側から一定間隔（100ms 程度）で呼ぶ。
    /// </summary>
    public void Tick()
    {
        var now = _clock.Now;

        // 終端文字を送らない機種のための確定。接続状態に関わらず行う。
        var flushed = _buffer.FlushIfIdle(now);
        if (flushed is not null)
        {
            RaiseScanned(flushed);
        }

        if (_settings is null || _state == ScannerState.Stopped)
        {
            return;
        }

        if (_state == ScannerState.Connected)
        {
            CheckPortStillPresent(now);
            return;
        }

        if (now >= _nextReconnectAt)
        {
            TryOpen();
        }
    }

    public async Task<ConnectionTestResult> TestConnectionAsync(
        SerialPortSettings settings, TimeSpan timeout, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!settings.HasPort)
        {
            return new ConnectionTestResult(false, "COM ポートを選択してください。");
        }

        // シリアルポートは排他利用。掴んだまま開き直すと必ず失敗するので、いったん手放す。
        var previous = _settings;
        Stop();

        try
        {
            using var port = _factory.Create();
            var received = new TaskCompletionSource<string>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var buffer = new ScanBuffer();

            port.DataReceived += (_, data) =>
            {
                foreach (var token in buffer.Append(data, _clock.Now))
                {
                    received.TrySetResult(token);
                }
            };

            port.Open(settings);

            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutSource.CancelAfter(timeout);

            try
            {
                var token = await received.Task.WaitAsync(timeoutSource.Token);

                return new ConnectionTestResult(
                    true, $"読み取りに成功しました。({settings.PortName})", token);
            }
            catch (OperationCanceledException)
            {
                // ポートは開けている。読み取りが無いだけなので、そう伝える。
                return new ConnectionTestResult(
                    false, "ポートは開けましたが、読み取りがありませんでした。QR をかざして再試行してください。");
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException
                                      or InvalidOperationException or ArgumentException)
        {
            _logger.LogWarning(ex, "接続テストに失敗しました: {Settings}", settings);

            return new ConnectionTestResult(false, DescribeOpenFailure(ex, settings));
        }
        finally
        {
            // テスト前の状態へ戻す。戻し忘れると打刻を受け付けなくなる。
            if (previous is not null)
            {
                Start(previous);
            }
        }
    }

    public void Dispose() => Stop();

    private void TryOpen()
    {
        var settings = _settings;
        if (settings is null)
        {
            return;
        }

        ClosePort();

        try
        {
            var port = _factory.Create();
            port.DataReceived += OnDataReceived;
            port.ErrorReceived += OnErrorReceived;
            port.Open(settings);

            _port = port;
            _reconnectAttempt = 0;
            _lastPortCheckAt = _clock.Now;

            _logger.LogInformation("QR リーダーに接続しました: {Settings}", settings);
            SetState(ScannerState.Connected);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException
                                      or InvalidOperationException or ArgumentException)
        {
            ScheduleReconnect(ex);
        }
    }

    private void CheckPortStillPresent(DateTime now)
    {
        if (now - _lastPortCheckAt < PortCheckInterval)
        {
            return;
        }

        _lastPortCheckAt = now;

        var settings = _settings;
        if (settings is null)
        {
            return;
        }

        // USB を抜くと、機種によっては例外もイベントも上がらないまま無反応になる。
        // 一覧から消えたかどうかで確実に検知する。
        if (!_enumerator.GetPortNames().Contains(settings.PortName, StringComparer.OrdinalIgnoreCase))
        {
            HandleDisconnect(new IOException($"{settings.PortName} が見つかりません。"));
        }
    }

    private void OnDataReceived(object? sender, byte[] data)
    {
        foreach (var token in _buffer.Append(data, _clock.Now))
        {
            RaiseScanned(token);
        }
    }

    private void OnErrorReceived(object? sender, Exception exception) => HandleDisconnect(exception);

    private void HandleDisconnect(Exception exception)
    {
        if (_state == ScannerState.Disconnected)
        {
            return;
        }

        _logger.LogWarning(exception, "QR リーダーとの接続が切れました。");

        ClosePort();
        _buffer.Reset();
        _reconnectAttempt = 0;

        ScheduleReconnect(exception, alreadyLogged: true);
    }

    private void ScheduleReconnect(Exception exception, bool alreadyLogged = false)
    {
        var delay = GetReconnectDelay(_reconnectAttempt);
        _nextReconnectAt = _clock.Now + delay;
        _reconnectAttempt++;

        if (!alreadyLogged)
        {
            _logger.LogWarning(
                exception, "接続できません。{Delay} 秒後に再試行します。", delay.TotalSeconds);
        }

        SetState(ScannerState.Disconnected);
    }

    private void ClosePort()
    {
        var port = _port;
        if (port is null)
        {
            return;
        }

        port.DataReceived -= OnDataReceived;
        port.ErrorReceived -= OnErrorReceived;
        port.Dispose();

        _port = null;
    }

    private void RaiseScanned(string token)
    {
        _logger.LogInformation("QR を読み取りました: {Fingerprint}", LogSafe.TokenFingerprint(token));
        Scanned?.Invoke(this, token);
    }

    private void SetState(ScannerState state)
    {
        bool changed;

        lock (_gate)
        {
            changed = _state != state;
            _state = state;
        }

        if (changed)
        {
            StateChanged?.Invoke(this, state);
        }
    }

    private static string DescribeOpenFailure(Exception exception, SerialPortSettings settings)
        => exception switch
        {
            UnauthorizedAccessException =>
                $"{settings.PortName} は使用中です。ほかのアプリを閉じてから再試行してください。",
            ArgumentException or IOException =>
                $"{settings.PortName} を開けません。ポート番号と通信設定を確認してください。",
            _ => $"{settings.PortName} に接続できません。",
        };
}
