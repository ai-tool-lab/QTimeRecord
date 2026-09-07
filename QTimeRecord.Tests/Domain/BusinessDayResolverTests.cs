using QTimeRecord.Core.Domain;

namespace QTimeRecord.Tests.Domain;

/// <summary>
/// 営業日の判定。このシステムで最も間違えやすく、間違えると給与がそのままずれる箇所。
/// </summary>
public sealed class BusinessDayResolverTests
{
    /// <summary>11:00 開店・翌 05:00 閉店。閉店中は 05:00〜11:00。</summary>
    private static readonly BusinessDaySettings NightStore = new(new TimeOnly(11, 0), new TimeOnly(5, 0));

    /// <summary>09:00〜18:00 の日中営業。</summary>
    private static readonly BusinessDaySettings DayStore = new(new TimeOnly(9, 0), new TimeOnly(18, 0));

    /// <summary>24時間営業。境界は 00:00。</summary>
    private static readonly BusinessDaySettings AroundTheClock = new(new TimeOnly(0, 0), new TimeOnly(0, 0));

    // ---- 日跨ぎ営業の基本 ----

    [Theory]
    [InlineData("2026-09-05 11:00", "2026-09-05")] // 開店ちょうど
    [InlineData("2026-09-05 22:00", "2026-09-05")] // 深夜勤務の出勤
    [InlineData("2026-09-05 23:59", "2026-09-05")]
    [InlineData("2026-09-06 00:00", "2026-09-05")] // 日付は変わるが同じ営業日
    [InlineData("2026-09-06 02:00", "2026-09-05")] // 退勤
    [InlineData("2026-09-06 04:59", "2026-09-05")] // 閉店直前
    [InlineData("2026-09-06 11:00", "2026-09-06")] // 翌営業日の開店
    public void 日跨ぎ営業の営業時間内(string recordedAt, string expected)
    {
        var actual = BusinessDayResolver.Resolve(Parse(recordedAt), NightStore);

        Assert.Equal(DateOnly.Parse(expected), actual);
    }

    [Fact]
    public void 開店1分前は前営業日として扱われる()
    {
        // 10:59 は閉店中。誰も勤務していなければ当日、勤務中なら前営業日（下のテスト）。
        Assert.False(BusinessDayResolver.IsWithinBusinessHours(Parse("2026-09-06 10:59"), NightStore));
    }

    // ---- 閉店中の振り分け（Q2-a） ----

    [Fact]
    public void 深夜勤務が延びた退勤は前営業日に付く()
    {
        // 9/5 22:00 出勤、未退勤のまま 9/6 06:00 に退勤（8時間経過）
        var openClockIn = Parse("2026-09-05 22:00");

        var actual = BusinessDayResolver.Resolve(Parse("2026-09-06 06:00"), NightStore, openClockIn);

        Assert.Equal(new DateOnly(2026, 9, 5), actual);
    }

    [Fact]
    public void 開店準備の出勤は当日に付く()
    {
        // 誰も勤務していない状態での 10:00 の出勤。前日の勤務に混ぜてはいけない。
        var actual = BusinessDayResolver.Resolve(Parse("2026-09-06 10:00"), NightStore, openClockInAt: null);

        Assert.Equal(new DateOnly(2026, 9, 6), actual);
    }

    [Fact]
    public void 早朝清掃の出勤も当日に付く()
    {
        var actual = BusinessDayResolver.Resolve(Parse("2026-09-06 06:00"), NightStore, openClockInAt: null);

        Assert.Equal(new DateOnly(2026, 9, 6), actual);
    }

    [Fact]
    public void 退勤忘れは勤務継続とみなさない()
    {
        // 9/5 11:00 に出勤して退勤し忘れ、9/6 10:00 に出勤打刻（23時間経過）。
        // 上限が無いと前営業日に付いてしまい、前日の勤務が異常に長くなる。
        var forgotten = Parse("2026-09-05 11:00");

        var actual = BusinessDayResolver.Resolve(Parse("2026-09-06 10:00"), NightStore, forgotten);

        Assert.Equal(new DateOnly(2026, 9, 6), actual);
    }

    [Theory]
    [InlineData(17.9, "2026-09-05")] // 上限内 → 前営業日
    [InlineData(18.0, "2026-09-05")] // ちょうど18時間 → 前営業日
    [InlineData(18.1, "2026-09-06")] // 超過 → 当日
    public void 勤務継続とみなす上限は18時間(double elapsedHours, string expected)
    {
        var recordedAt = Parse("2026-09-06 06:00");
        var openClockIn = recordedAt.AddHours(-elapsedHours);

        var actual = BusinessDayResolver.Resolve(recordedAt, NightStore, openClockIn);

        Assert.Equal(DateOnly.Parse(expected), actual);
    }

    [Fact]
    public void 未来の出勤日時は勤務継続とみなさない()
    {
        // 時計のずれや手修正で、出勤が打刻より後になっている場合。
        var future = Parse("2026-09-06 12:00");

        var actual = BusinessDayResolver.Resolve(Parse("2026-09-06 06:00"), NightStore, future);

        Assert.Equal(new DateOnly(2026, 9, 6), actual);
    }

    // ---- 日中営業 ----

    [Theory]
    [InlineData("2026-09-05 09:00", "2026-09-05")]
    [InlineData("2026-09-05 17:59", "2026-09-05")]
    [InlineData("2026-09-05 20:00", "2026-09-05")] // 閉店後の残業
    public void 日中営業(string recordedAt, string expected)
    {
        var actual = BusinessDayResolver.Resolve(Parse(recordedAt), DayStore);

        Assert.Equal(DateOnly.Parse(expected), actual);
    }

    [Fact]
    public void 日中営業の営業時間判定()
    {
        Assert.True(BusinessDayResolver.IsWithinBusinessHours(Parse("2026-09-05 09:00"), DayStore));
        Assert.True(BusinessDayResolver.IsWithinBusinessHours(Parse("2026-09-05 17:59"), DayStore));

        // 終了時刻ちょうどは営業時間外
        Assert.False(BusinessDayResolver.IsWithinBusinessHours(Parse("2026-09-05 18:00"), DayStore));
        Assert.False(BusinessDayResolver.IsWithinBusinessHours(Parse("2026-09-05 08:59"), DayStore));
    }

    // ---- 24時間営業 ----

    [Theory]
    [InlineData("2026-09-05 00:00", "2026-09-05")]
    [InlineData("2026-09-05 12:00", "2026-09-05")]
    [InlineData("2026-09-05 23:59", "2026-09-05")]
    public void 終日営業は常に営業時間内(string recordedAt, string expected)
    {
        var at = Parse(recordedAt);

        Assert.True(BusinessDayResolver.IsWithinBusinessHours(at, AroundTheClock));
        Assert.Equal(DateOnly.Parse(expected), BusinessDayResolver.Resolve(at, AroundTheClock));
    }

    // ---- 日付の境界 ----

    [Fact]
    public void 月をまたぐ()
    {
        // 9/30 23:00 出勤 → 10/1 02:00 退勤は 9月30日の営業日
        Assert.Equal(
            new DateOnly(2026, 9, 30),
            BusinessDayResolver.Resolve(Parse("2026-10-01 02:00"), NightStore));
    }

    [Fact]
    public void 年をまたぐ()
    {
        Assert.Equal(
            new DateOnly(2026, 12, 31),
            BusinessDayResolver.Resolve(Parse("2027-01-01 03:00"), NightStore));
    }

    [Fact]
    public void うるう日をまたぐ()
    {
        // 2028年はうるう年。3/1 02:00 の打刻は 2/29 の営業日。
        Assert.Equal(
            new DateOnly(2028, 2, 29),
            BusinessDayResolver.Resolve(Parse("2028-03-01 02:00"), NightStore));
    }

    // ---- 設定の性質 ----

    [Fact]
    public void 設定の分類()
    {
        Assert.True(NightStore.CrossesMidnight);
        Assert.False(NightStore.IsAroundTheClock);

        Assert.False(DayStore.CrossesMidnight);
        Assert.False(DayStore.IsAroundTheClock);

        Assert.True(AroundTheClock.IsAroundTheClock);
    }

    [Fact]
    public void 店舗設定から作れる()
    {
        var store = new Store
        {
            Id = Guid.CreateVersion7(),
            StoreName = "テスト店",
            CompanyName = "テスト株式会社",
            BusinessDayStart = new TimeOnly(11, 0),
            BusinessDayEnd = new TimeOnly(5, 0),
        };

        Assert.Equal(NightStore, BusinessDaySettings.From(store));
    }

    private static DateTime Parse(string value)
        => DateTime.ParseExact(value, "yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);
}
