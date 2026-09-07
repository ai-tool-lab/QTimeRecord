using QTimeRecord.Core.Domain;

namespace QTimeRecord.Tests.Domain;

public sealed class PunchStateMachineTests
{
    private static readonly DateTime Now = new(2026, 9, 6, 12, 0, 0);

    // ---- 状態の導出 ----

    [Fact]
    public void 打刻が無ければ未出勤()
    {
        Assert.Equal(PunchState.NotClockedIn, PunchStateMachine.Resolve([]));
    }

    [Theory]
    [InlineData(TimeRecordType.ClockIn, PunchState.Working)]
    [InlineData(TimeRecordType.ClockOut, PunchState.ClockedOut)]
    [InlineData(TimeRecordType.BreakStart, PunchState.OnBreak)]
    [InlineData(TimeRecordType.BreakEnd, PunchState.Working)]
    public void 最後の打刻で状態が決まる(TimeRecordType last, PunchState expected)
    {
        var records = new[] { Record(TimeRecordType.ClockIn, Now.AddHours(-3)), Record(last, Now) };

        Assert.Equal(expected, PunchStateMachine.Resolve(records));
    }

    [Fact]
    public void 順不同で渡しても打刻時刻の順で判定する()
    {
        // Repository の並び順に依存すると、取得方法を変えた瞬間に状態が壊れる。
        var records = new[]
        {
            Record(TimeRecordType.ClockOut, Now),
            Record(TimeRecordType.ClockIn, Now.AddHours(-8)),
        };

        Assert.Equal(PunchState.ClockedOut, PunchStateMachine.Resolve(records));
    }

    [Fact]
    public void 日をまたいでも最後の打刻で状態が決まる()
    {
        // 22:00 出勤 → 翌 02:00 退勤。同じ営業日の打刻として渡される。
        var records = new[]
        {
            Record(TimeRecordType.ClockIn, new DateTime(2026, 9, 6, 22, 0, 0)),
            Record(TimeRecordType.BreakStart, new DateTime(2026, 9, 7, 0, 30, 0)),
            Record(TimeRecordType.BreakEnd, new DateTime(2026, 9, 7, 1, 0, 0)),
        };

        // 日付ではなく打刻時刻の順で見る。日付で見ると 22:00 が最後になる。
        Assert.Equal(PunchState.Working, PunchStateMachine.Resolve(records));
    }

    // ---- 遷移表（plan.md 9-3）全16パターン ----

    [Theory]
    // 未出勤
    [InlineData(PunchState.NotClockedIn, TimeRecordType.ClockIn, PunchDecision.Normal)]
    [InlineData(PunchState.NotClockedIn, TimeRecordType.ClockOut, PunchDecision.Warning)]
    [InlineData(PunchState.NotClockedIn, TimeRecordType.BreakStart, PunchDecision.Warning)]
    [InlineData(PunchState.NotClockedIn, TimeRecordType.BreakEnd, PunchDecision.Warning)]
    // 勤務中
    [InlineData(PunchState.Working, TimeRecordType.ClockIn, PunchDecision.Warning)]
    [InlineData(PunchState.Working, TimeRecordType.ClockOut, PunchDecision.Normal)]
    [InlineData(PunchState.Working, TimeRecordType.BreakStart, PunchDecision.Normal)]
    [InlineData(PunchState.Working, TimeRecordType.BreakEnd, PunchDecision.Warning)]
    // 中抜け中
    [InlineData(PunchState.OnBreak, TimeRecordType.ClockIn, PunchDecision.Warning)]
    [InlineData(PunchState.OnBreak, TimeRecordType.ClockOut, PunchDecision.Warning)]
    [InlineData(PunchState.OnBreak, TimeRecordType.BreakStart, PunchDecision.Warning)]
    [InlineData(PunchState.OnBreak, TimeRecordType.BreakEnd, PunchDecision.Normal)]
    // 退勤済（再出勤も警告のみ。禁止しない → plan.md Q8）
    [InlineData(PunchState.ClockedOut, TimeRecordType.ClockIn, PunchDecision.Warning)]
    [InlineData(PunchState.ClockedOut, TimeRecordType.ClockOut, PunchDecision.Warning)]
    [InlineData(PunchState.ClockedOut, TimeRecordType.BreakStart, PunchDecision.Warning)]
    [InlineData(PunchState.ClockedOut, TimeRecordType.BreakEnd, PunchDecision.Warning)]
    public void 遷移表のとおりに判定する(
        PunchState state, TimeRecordType type, PunchDecision expected)
    {
        var judgement = PunchStateMachine.Evaluate(state, type, latest: null, Now);

        Assert.Equal(expected, judgement.Decision);
    }

    [Theory]
    [InlineData(PunchState.NotClockedIn, TimeRecordType.ClockOut)]
    [InlineData(PunchState.Working, TimeRecordType.ClockIn)]
    [InlineData(PunchState.OnBreak, TimeRecordType.BreakStart)]
    [InlineData(PunchState.ClockedOut, TimeRecordType.ClockIn)]
    public void 警告には理由が付く(PunchState state, TimeRecordType type)
    {
        // 理由が無いと、確認ダイアログが「よろしいですか」だけになり判断できない。
        var judgement = PunchStateMachine.Evaluate(state, type, latest: null, Now);

        Assert.True(judgement.NeedsConfirmation);
        Assert.False(string.IsNullOrWhiteSpace(judgement.Reason));
    }

    [Fact]
    public void 正常な打刻に理由は付かない()
    {
        var judgement = PunchStateMachine.Evaluate(
            PunchState.Working, TimeRecordType.ClockOut, latest: null, Now);

        Assert.Equal(PunchDecision.Normal, judgement.Decision);
        Assert.Null(judgement.Reason);
        Assert.False(judgement.NeedsConfirmation);
    }

    // ---- 60秒の重複 ----

    [Theory]
    [InlineData(0)]
    [InlineData(59)]
    [InlineData(60)]
    public void 同一種別を60秒以内に繰り返すと重複になる(int seconds)
    {
        var latest = Record(TimeRecordType.ClockIn, Now.AddSeconds(-seconds));

        var judgement = PunchStateMachine.Evaluate(
            PunchState.Working, TimeRecordType.ClockIn, latest, Now);

        Assert.Equal(PunchDecision.Duplicate, judgement.Decision);
    }

    [Fact]
    public void 六十一秒後の同一種別は重複にならない()
    {
        // 意図して押し直した打刻まで捨てると、記録が欠ける。
        var latest = Record(TimeRecordType.ClockIn, Now.AddSeconds(-61));

        var judgement = PunchStateMachine.Evaluate(
            PunchState.Working, TimeRecordType.ClockIn, latest, Now);

        Assert.Equal(PunchDecision.Warning, judgement.Decision);
    }

    [Fact]
    public void 種別が違えば直後でも重複にならない()
    {
        var latest = Record(TimeRecordType.ClockIn, Now.AddSeconds(-1));

        var judgement = PunchStateMachine.Evaluate(
            PunchState.Working, TimeRecordType.BreakStart, latest, Now);

        Assert.Equal(PunchDecision.Normal, judgement.Decision);
    }

    [Fact]
    public void 未来の打刻が残っていても打刻できる()
    {
        // 時計を戻した端末や手修正で、直近が未来になることがある。
        // これを重複扱いにすると、その端末では二度と打刻できなくなる。
        var latest = Record(TimeRecordType.ClockIn, Now.AddMinutes(30));

        var judgement = PunchStateMachine.Evaluate(
            PunchState.Working, TimeRecordType.ClockIn, latest, Now);

        Assert.NotEqual(PunchDecision.Duplicate, judgement.Decision);
    }

    [Fact]
    public void 重複は遷移が正常でも記録しない()
    {
        // 二度読みは「正常な遷移」の形でも起きる（出勤直後にもう一度出勤など）。
        var latest = Record(TimeRecordType.ClockIn, Now.AddSeconds(-5));

        var judgement = PunchStateMachine.Evaluate(
            PunchState.NotClockedIn, TimeRecordType.ClockIn, latest, Now);

        Assert.Equal(PunchDecision.Duplicate, judgement.Decision);
    }

    private static TimeRecord Record(TimeRecordType type, DateTime recordedAt) => new()
    {
        Id = Guid.CreateVersion7(),
        StoreId = Guid.CreateVersion7(),
        StaffId = Guid.CreateVersion7(),
        RecordType = type,
        RecordedAt = recordedAt,
        WorkDate = DateOnly.FromDateTime(recordedAt),
        EntryMethod = EntryMethod.Qr,
    };
}
