using QTimeRecord.App.Services;

namespace QTimeRecord.Tests;

/// <summary>
/// 好きなタイミングで進められるティッカー。
/// 実時間を待たずに、時計の更新・エラーの自動消去・無操作の時間切れを確かめる。
/// </summary>
public sealed class StubTicker : IUiTicker
{
    private EventHandler? _tick;

    public event EventHandler? Tick
    {
        add
        {
            _tick += value;
            SubscriberCount++;
        }
        remove
        {
            _tick -= value;
            SubscriberCount--;
        }
    }

    /// <summary>購読の数。止め忘れ（リーク）の検出に使う。</summary>
    public int SubscriberCount { get; private set; }

    public void Start()
    {
    }

    public void Stop()
    {
    }

    public void Raise() => _tick?.Invoke(this, EventArgs.Empty);
}
