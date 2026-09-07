using QTimeRecord.App.Services;
using QTimeRecord.App.ViewModels;
using QTimeRecord.Core.Domain;

namespace QTimeRecord.Tests.ViewModels;

public sealed class PunchResultViewModelTests
{
    private static readonly DateTime RecordedAt = new(2026, 9, 6, 8, 58, 43);

    [Theory]
    [InlineData(TimeRecordType.ClockIn, "出勤を記録しました")]
    [InlineData(TimeRecordType.ClockOut, "退勤を記録しました")]
    [InlineData(TimeRecordType.BreakStart, "中抜け開始を記録しました")]
    [InlineData(TimeRecordType.BreakEnd, "中抜け終了を記録しました")]
    public void 打刻種別ごとに完了の文言が変わる(TimeRecordType type, string expected)
    {
        var (viewModel, _, _) = Create();

        viewModel.Show(NewStaff(), NewRecord(type));

        Assert.Equal(expected, viewModel.Headline);
        Assert.Equal("山田 太郎", viewModel.StaffName);
        Assert.Equal(RecordedAt, viewModel.RecordedAt);
    }

    [Fact]
    public void 三秒で待機画面へ戻る()
    {
        var (viewModel, clock, ticker) = Create();

        var finished = false;
        viewModel.Finished += (_, _) => finished = true;

        viewModel.Show(NewStaff(), NewRecord(TimeRecordType.ClockIn));

        clock.Advance(IdleTimeoutService.ResultTimeout - TimeSpan.FromSeconds(1));
        ticker.Raise();
        Assert.False(finished);

        clock.Advance(TimeSpan.FromSeconds(1));
        ticker.Raise();

        // 「閉じる」を押す人はいない。自動で戻さないと次の人が打刻できない。
        Assert.True(finished);
    }

    [Fact]
    public void 営業時間外の打刻はその場で伝える()
    {
        var (viewModel, _, _) = Create();

        var record = NewRecord(TimeRecordType.ClockIn);
        record.IsOutsideBusinessHours = true;

        viewModel.Show(NewStaff(), record);

        // あとで管理者に確認されることを、本人がその場で知れるようにする。
        Assert.True(viewModel.IsOutsideBusinessHours);
    }

    [Fact]
    public void 破棄するとタイマーが残らない()
    {
        var (viewModel, _, ticker) = Create();

        viewModel.Show(NewStaff(), NewRecord(TimeRecordType.ClockIn));
        viewModel.Dispose();

        Assert.Equal(0, ticker.SubscriberCount);
    }

    private static (PunchResultViewModel ViewModel, TestClock Clock, StubTicker Ticker) Create()
    {
        var clock = new TestClock();
        var ticker = new StubTicker();

        return (new PunchResultViewModel(new IdleTimeoutService(clock, ticker)), clock, ticker);
    }

    private static Staff NewStaff() => new()
    {
        Id = Guid.CreateVersion7(),
        StoreId = Guid.CreateVersion7(),
        Name = "山田 太郎",
        Status = StaffStatus.Active,
    };

    private static TimeRecord NewRecord(TimeRecordType type) => new()
    {
        Id = Guid.CreateVersion7(),
        StoreId = Guid.CreateVersion7(),
        StaffId = Guid.CreateVersion7(),
        RecordType = type,
        RecordedAt = RecordedAt,
        WorkDate = DateOnly.FromDateTime(RecordedAt),
        EntryMethod = EntryMethod.Qr,
    };
}
