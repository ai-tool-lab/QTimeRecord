using QTimeRecord.Core.Domain;

namespace QTimeRecord.Tests.Domain;

/// <summary>
/// 列挙型と DB 保存値の相互変換。
///
/// ここがずれると、保存済みの打刻を読み出した瞬間に例外になる。
/// 値を1つ足したときに壊れていないかを、全要素の総当たりで確認する。
/// </summary>
public sealed class EnumDbValueTests
{
    public static TheoryData<StaffStatus, string> 在籍状態 => new()
    {
        { StaffStatus.Active, "ACTIVE" },
        { StaffStatus.OnLeave, "ON_LEAVE" },
        { StaffStatus.Retired, "RETIRED" },
    };

    public static TheoryData<TimeRecordType, string> 打刻種別 => new()
    {
        { TimeRecordType.ClockIn, "CLOCK_IN" },
        { TimeRecordType.ClockOut, "CLOCK_OUT" },
        { TimeRecordType.BreakStart, "BREAK_START" },
        { TimeRecordType.BreakEnd, "BREAK_END" },
    };

    public static TheoryData<EntryMethod, string> 登録方法 => new()
    {
        { EntryMethod.Qr, "QR" },
        { EntryMethod.ManualAdd, "MANUAL_ADD" },
        { EntryMethod.ManualEdit, "MANUAL_EDIT" },
    };

    [Theory]
    [MemberData(nameof(在籍状態))]
    public void 在籍状態が期待どおりの文字列になる(StaffStatus value, string expected)
    {
        Assert.Equal(expected, EnumDbValue.ToDbValue(value));
        Assert.Equal(value, EnumDbValue.Parse<StaffStatus>(expected));
    }

    [Theory]
    [MemberData(nameof(打刻種別))]
    public void 打刻種別が期待どおりの文字列になる(TimeRecordType value, string expected)
    {
        Assert.Equal(expected, EnumDbValue.ToDbValue(value));
        Assert.Equal(value, EnumDbValue.Parse<TimeRecordType>(expected));
    }

    [Theory]
    [MemberData(nameof(登録方法))]
    public void 登録方法が期待どおりの文字列になる(EntryMethod value, string expected)
    {
        Assert.Equal(expected, EnumDbValue.ToDbValue(value));
        Assert.Equal(value, EnumDbValue.Parse<EntryMethod>(expected));
    }

    [Fact]
    public void すべての列挙値が往復変換できる()
    {
        // 値を追加したときに対応表の更新漏れを検出する。
        AssertRoundTrip<StaffStatus>();
        AssertRoundTrip<TimeRecordType>();
        AssertRoundTrip<EntryMethod>();
    }

    [Fact]
    public void 未知の文字列は例外になる()
    {
        // 黙って既定値へ倒すと、壊れたデータが正常な打刻として扱われる。
        Assert.Throws<ArgumentOutOfRangeException>(() => EnumDbValue.Parse<TimeRecordType>("CLOCKIN"));
        Assert.Throws<ArgumentOutOfRangeException>(() => EnumDbValue.Parse<TimeRecordType>("clock_in"));
        Assert.Throws<ArgumentOutOfRangeException>(() => EnumDbValue.Parse<StaffStatus>(string.Empty));
    }

    [Fact]
    public void 定義外の列挙値は例外になる()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => EnumDbValue.ToDbValue((StaffStatus)999));
    }

    private static void AssertRoundTrip<TEnum>() where TEnum : struct, Enum
    {
        foreach (var value in Enum.GetValues<TEnum>())
        {
            var dbValue = EnumDbValue.ToDbValue(value);

            Assert.False(string.IsNullOrWhiteSpace(dbValue));
            Assert.Equal(dbValue, dbValue.ToUpperInvariant());
            Assert.Equal(value, EnumDbValue.Parse<TEnum>(dbValue));
        }
    }
}
