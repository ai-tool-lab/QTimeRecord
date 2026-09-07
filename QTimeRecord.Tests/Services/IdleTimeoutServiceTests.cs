using QTimeRecord.App.Services;

namespace QTimeRecord.Tests.Services;

public sealed class IdleTimeoutServiceTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    [Fact]
    public void 時間切れで一度だけ通知する()
    {
        var clock = new TestClock();
        var ticker = new StubTicker();
        using var service = new IdleTimeoutService(clock, ticker);

        var elapsed = 0;
        service.Elapsed += (_, _) => elapsed++;
        service.Start(Timeout);

        clock.Advance(Timeout);
        ticker.Raise();
        ticker.Raise();

        // 2回出ると、画面が二段飛ばしで戻る。
        Assert.Equal(1, elapsed);
    }

    [Fact]
    public void 時間内は通知しない()
    {
        var clock = new TestClock();
        var ticker = new StubTicker();
        using var service = new IdleTimeoutService(clock, ticker);

        var elapsed = false;
        service.Elapsed += (_, _) => elapsed = true;
        service.Start(Timeout);

        clock.Advance(Timeout - TimeSpan.FromSeconds(1));
        ticker.Raise();

        Assert.False(elapsed);
        Assert.Equal(1, service.RemainingSeconds);
    }

    [Fact]
    public void 操作でカウントが戻る()
    {
        var clock = new TestClock();
        var ticker = new StubTicker();
        using var service = new IdleTimeoutService(clock, ticker);

        var elapsed = false;
        service.Elapsed += (_, _) => elapsed = true;
        service.Start(Timeout);

        clock.Advance(TimeSpan.FromSeconds(29));
        ticker.Raise();
        service.Reset();

        clock.Advance(TimeSpan.FromSeconds(29));
        ticker.Raise();

        Assert.False(elapsed);
        Assert.Equal(1, service.RemainingSeconds);
    }

    [Fact]
    public void 残り秒数は秒が変わったときだけ通知する()
    {
        var clock = new TestClock();
        var ticker = new StubTicker();
        using var service = new IdleTimeoutService(clock, ticker);

        service.Start(Timeout);

        // 開始時の1回は数えない。ここで見たいのは経過中の頻度。
        var notified = 0;
        service.Remaining += (_, _) => notified++;

        // 100ms ごとに通知すると、1秒あたり10回の再描画になる。
        for (var i = 0; i < 10; i++)
        {
            clock.Advance(TimeSpan.FromMilliseconds(100));
            ticker.Raise();
        }

        Assert.Equal(1, notified);
        Assert.Equal(29, service.RemainingSeconds);
    }

    [Fact]
    public void 何度開始しても購読は1つ()
    {
        var clock = new TestClock();
        var ticker = new StubTicker();
        using var service = new IdleTimeoutService(clock, ticker);

        service.Start(Timeout);
        service.Start(Timeout);
        service.Start(Timeout);

        var elapsed = 0;
        service.Elapsed += (_, _) => elapsed++;

        clock.Advance(Timeout);
        ticker.Raise();

        Assert.Equal(1, elapsed);
        Assert.Equal(0, ticker.SubscriberCount);
    }

    [Fact]
    public void 破棄すると時間切れが起きない()
    {
        var clock = new TestClock();
        var ticker = new StubTicker();
        var service = new IdleTimeoutService(clock, ticker);

        var elapsed = false;
        service.Elapsed += (_, _) => elapsed = true;
        service.Start(Timeout);

        service.Dispose();

        clock.Advance(Timeout);
        ticker.Raise();

        // 画面を離れたあとに発火すると、次の画面が勝手に巻き戻る。
        Assert.False(elapsed);
        Assert.Equal(0, ticker.SubscriberCount);
    }

    [Fact]
    public void 時間切れのあとは購読を残さない()
    {
        var clock = new TestClock();
        var ticker = new StubTicker();
        using var service = new IdleTimeoutService(clock, ticker);

        service.Start(Timeout);

        clock.Advance(Timeout);
        ticker.Raise();

        Assert.Equal(0, ticker.SubscriberCount);
    }

    [Fact]
    public void 残り秒数は0を下回らない()
    {
        var clock = new TestClock();
        var ticker = new StubTicker();
        using var service = new IdleTimeoutService(clock, ticker);

        service.Start(Timeout);

        clock.Advance(Timeout + TimeSpan.FromSeconds(10));
        ticker.Raise();

        // 負の秒数を画面に出さない。
        Assert.Equal(0, service.RemainingSeconds);
    }

    [Fact]
    public void 破棄後の開始は例外になる()
    {
        var service = new IdleTimeoutService(new TestClock(), new StubTicker());
        service.Dispose();

        Assert.Throws<ObjectDisposedException>(() => service.Start(Timeout));
    }
}
