using QTimeRecord.Core.Infrastructure;

namespace QTimeRecord.App.Services;

/// <summary>
/// 無操作で自動的に戻すためのタイマー。
///
/// キオスク端末は「操作の途中で人が立ち去る」ことを前提にする。
/// 打刻画面が他人の名前を出したまま残ると、次の人が誤って打刻してしまう。
///
/// <b>共有の <see cref="IUiTicker"/> に相乗りする。</b>
/// 画面ごとに <c>DispatcherTimer</c> を持つと、遷移のたびに止め忘れが起き、
/// 裏で動き続けたタイマーが前の画面へ戻そうとする。
/// 使い終わったら必ず <see cref="Dispose"/> すること。
/// </summary>
public sealed class IdleTimeoutService(IClock clock, IUiTicker ticker) : IDisposable
{
    /// <summary>打刻種別の選択を待つ時間（→ plan.md 9-2）。</summary>
    public static readonly TimeSpan SelectionTimeout = TimeSpan.FromSeconds(30);

    /// <summary>打刻完了の表示時間（→ plan.md 9-4-5）。</summary>
    public static readonly TimeSpan ResultTimeout = TimeSpan.FromSeconds(3);

    private TimeSpan _timeout = SelectionTimeout;
    private DateTime _startedAt;
    private bool _running;
    private bool _disposed;

    /// <summary>時間切れ。UI スレッドから1回だけ発生する。</summary>
    public event EventHandler? Elapsed;

    /// <summary>残り時間が変わった。カウントダウン表示の更新に使う。</summary>
    public event EventHandler<TimeSpan>? Remaining;

    /// <summary>残り時間。切り上げた秒数を画面に出す。</summary>
    public TimeSpan RemainingTime { get; private set; }

    /// <summary>残り秒数（切り上げ）。0 を下回らない。</summary>
    public int RemainingSeconds => (int)Math.Ceiling(Math.Max(RemainingTime.TotalSeconds, 0));

    /// <summary>計測を始める。すでに動いていれば起点だけを戻す。</summary>
    public void Start(TimeSpan timeout)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _timeout = timeout;

        if (!_running)
        {
            // 二重に購読すると Elapsed が2回出て、画面が二段飛ばしで戻る。
            ticker.Tick += OnTick;
            _running = true;
        }

        Reset();
    }

    /// <summary>起点を今に戻す。何か操作があったら呼ぶ。</summary>
    public void Reset()
    {
        _startedAt = clock.Now;
        UpdateRemaining(_timeout);
    }

    public void Stop()
    {
        if (!_running)
        {
            return;
        }

        ticker.Tick -= OnTick;
        _running = false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var remaining = _timeout - (clock.Now - _startedAt);

        UpdateRemaining(remaining);

        if (remaining > TimeSpan.Zero)
        {
            return;
        }

        // 先に止める。Elapsed の中で画面が切り替わる間に、もう一度発生させないため。
        Stop();
        Elapsed?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateRemaining(TimeSpan remaining)
    {
        var previous = RemainingSeconds;

        RemainingTime = remaining;

        // 秒が変わったときだけ通知する。100ms ごとに出すと無駄に再描画される。
        if (RemainingSeconds != previous)
        {
            Remaining?.Invoke(this, RemainingTime);
        }
    }
}
