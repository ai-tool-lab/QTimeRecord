using System.Windows.Threading;

namespace QTimeRecord.App.Services;

public interface IUiTicker
{
    /// <summary>一定間隔で発生する。UI スレッド上で呼ばれる。</summary>
    event EventHandler? Tick;

    void Start();

    void Stop();
}

/// <summary>
/// 画面まわりの定期処理をまとめる。
///
/// タイマーを画面ごとに持つと、遷移のたびに止め忘れが起きて裏で動き続ける。
/// 1本のタイマーを共有し、購読する側が必要な間隔で間引く。
/// </summary>
public sealed class UiTicker : IUiTicker, IDisposable
{
    /// <summary>
    /// 100ms。時計は1秒単位で足りるが、QR の受信確定（終端なし機種の 200ms）に
    /// 追随するにはこの程度の粒度が要る。
    /// </summary>
    public static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(100);

    private readonly DispatcherTimer _timer = new(DispatcherPriority.Normal)
    {
        Interval = Interval,
    };

    public UiTicker()
    {
        _timer.Tick += (_, _) => Tick?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? Tick;

    public void Start() => _timer.Start();

    public void Stop() => _timer.Stop();

    public void Dispose() => Stop();
}
