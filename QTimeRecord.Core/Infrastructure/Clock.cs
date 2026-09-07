namespace QTimeRecord.Core.Infrastructure;

/// <summary>
/// 現在時刻。
///
/// ロジックが <c>DateTime.Now</c> を直接呼ぶと、営業日の判定・状態遷移・集計を
/// 固定時刻でテストできなくなる。日跨ぎや月末はテストでしか確認できないため、
/// 時刻の取得は必ずここを通す。
/// </summary>
public interface IClock
{
    /// <summary>端末のローカル時刻。打刻の記録にそのまま使う（→ plan.md 5-3）。</summary>
    DateTime Now { get; }

    /// <summary>今日のカレンダー日付。営業日ではない。</summary>
    DateOnly Today => DateOnly.FromDateTime(Now);
}

public sealed class SystemClock : IClock
{
    public DateTime Now => DateTime.Now;
}
