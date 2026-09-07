using QTimeRecord.Core.Infrastructure;

namespace QTimeRecord.Tests;

/// <summary>
/// 時刻を固定・前進させられる時計。
/// 再接続の待ち時間や営業日の境界は、実時間を待たずに検証する。
/// </summary>
public sealed class TestClock(DateTime? start = null) : IClock
{
    public DateTime Now { get; private set; } = start ?? new DateTime(2026, 9, 6, 12, 0, 0);

    public void Advance(TimeSpan amount) => Now += amount;

    public void AdvanceSeconds(double seconds) => Advance(TimeSpan.FromSeconds(seconds));
}
