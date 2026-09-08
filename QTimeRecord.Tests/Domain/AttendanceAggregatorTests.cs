using QTimeRecord.Core.Domain;

namespace QTimeRecord.Tests.Domain;

public sealed class AttendanceAggregatorTests
{
    private static readonly Guid StaffId = Guid.CreateVersion7();
    private static readonly DateOnly WorkDate = new(2026, 9, 6);

    [Fact]
    public void 出勤と退勤だけなら実労働はその差()
    {
        var summary = Summarize(
            (TimeRecordType.ClockIn, "09:00"),
            (TimeRecordType.ClockOut, "18:00"));

        Assert.Equal(At("09:00"), summary.ClockInAt);
        Assert.Equal(At("18:00"), summary.ClockOutAt);
        Assert.Equal(540, summary.WorkedMinutes);
        Assert.Equal(0, summary.BreakCount);
        Assert.Equal(AttendanceStatus.Normal, summary.Status);
    }

    [Fact]
    public void 中抜けの分だけ実労働から引く()
    {
        var summary = Summarize(
            (TimeRecordType.ClockIn, "09:00"),
            (TimeRecordType.BreakStart, "12:00"),
            (TimeRecordType.BreakEnd, "13:00"),
            (TimeRecordType.ClockOut, "18:00"));

        Assert.Equal(1, summary.BreakCount);
        Assert.Equal(60, summary.BreakMinutes);
        Assert.Equal(480, summary.WorkedMinutes);
        Assert.Equal(AttendanceStatus.Normal, summary.Status);
    }

    [Fact]
    public void 中抜けが複数回でも一行にまとめる()
    {
        // 1日1行を維持し、回数と合計を持つ（→ plan.md Q3）。
        var summary = Summarize(
            (TimeRecordType.ClockIn, "09:00"),
            (TimeRecordType.BreakStart, "12:00"),
            (TimeRecordType.BreakEnd, "12:30"),
            (TimeRecordType.BreakStart, "15:00"),
            (TimeRecordType.BreakEnd, "15:15"),
            (TimeRecordType.ClockOut, "18:00"));

        Assert.Equal(2, summary.BreakCount);
        Assert.Equal(45, summary.BreakMinutes);

        // 一覧の「中抜け開始／終了」は最初と最後を出す。
        Assert.Equal(At("12:00"), summary.BreakStartAt);
        Assert.Equal(At("15:15"), summary.BreakEndAt);
        Assert.Equal(495, summary.WorkedMinutes);
    }

    [Fact]
    public void 退勤を忘れた日は実労働を出さず要確認にする()
    {
        var summary = Summarize((TimeRecordType.ClockIn, "09:00"));

        // 「いま現在まで働いている」として数えると、翌月になっても増え続ける。
        Assert.Null(summary.WorkedMinutes);
        Assert.True(summary.IsIncomplete);
        Assert.Equal(AttendanceStatus.NeedsReview, summary.Status);
    }

    [Fact]
    public void 出勤がなく退勤だけでも例外にしない()
    {
        var summary = Summarize((TimeRecordType.ClockOut, "18:00"));

        Assert.Null(summary.ClockInAt);
        Assert.Null(summary.WorkedMinutes);
        Assert.Equal(AttendanceStatus.NeedsReview, summary.Status);
    }

    [Fact]
    public void 中抜け終了を忘れた分は合計に入れず要確認にする()
    {
        var summary = Summarize(
            (TimeRecordType.ClockIn, "09:00"),
            (TimeRecordType.BreakStart, "12:00"),
            (TimeRecordType.ClockOut, "18:00"));

        Assert.Equal(0, summary.BreakCount);
        Assert.Equal(0, summary.BreakMinutes);
        Assert.Equal(540, summary.WorkedMinutes);
        Assert.Equal(AttendanceStatus.NeedsReview, summary.Status);
    }

    [Fact]
    public void 異常な遷移がある日は要確認になる()
    {
        // 出勤 → 出勤。打刻時に警告して記録したもの（→ plan.md Q7）。
        var summary = Summarize(
            (TimeRecordType.ClockIn, "09:00"),
            (TimeRecordType.ClockIn, "09:30"),
            (TimeRecordType.ClockOut, "18:00"));

        Assert.Equal(AttendanceStatus.NeedsReview, summary.Status);
        Assert.False(summary.IsIncomplete);
    }

    [Fact]
    public void 打刻が無ければ要確認にしない()
    {
        var summary = AttendanceAggregator.Summarize(StaffId, WorkDate, []);

        // 行そのものが存在しない日を「要確認」にすると、一覧が警告で埋まる。
        Assert.Equal(AttendanceStatus.Normal, summary.Status);
        Assert.Null(summary.WorkedMinutes);
    }

    [Fact]
    public void 日をまたぐ勤務も一日として計算する()
    {
        // 22:00 出勤 → 翌 02:00 退勤。営業日は同じ 9/6。
        var records = new[]
        {
            Record(TimeRecordType.ClockIn, new DateTime(2026, 9, 6, 22, 0, 0)),
            Record(TimeRecordType.ClockOut, new DateTime(2026, 9, 7, 2, 0, 0)),
        };

        var summary = AttendanceAggregator.Summarize(StaffId, WorkDate, records);

        Assert.Equal(240, summary.WorkedMinutes);
        Assert.Equal(AttendanceStatus.Normal, summary.Status);
    }

    [Fact]
    public void 順不同で渡しても打刻時刻の順で畳む()
    {
        var records = new[]
        {
            Record(TimeRecordType.ClockOut, At("18:00")),
            Record(TimeRecordType.ClockIn, At("09:00")),
            Record(TimeRecordType.BreakEnd, At("13:00")),
            Record(TimeRecordType.BreakStart, At("12:00")),
        };

        var summary = AttendanceAggregator.Summarize(StaffId, WorkDate, records);

        Assert.Equal(1, summary.BreakCount);
        Assert.Equal(60, summary.BreakMinutes);
        Assert.Equal(480, summary.WorkedMinutes);
    }

    [Fact]
    public void 手入力と手修正を見分ける()
    {
        var records = new[]
        {
            Record(TimeRecordType.ClockIn, At("09:00"), EntryMethod.ManualAdd),
            Record(TimeRecordType.ClockOut, At("18:00")),
        };

        var summary = AttendanceAggregator.Summarize(StaffId, WorkDate, records);

        Assert.True(summary.HasManualAdd);
        Assert.False(summary.HasManualEdit);
        Assert.Equal(AttendanceStatus.ManualAdd, summary.Status);
    }

    [Fact]
    public void 手入力より要確認を優先して出す()
    {
        // バッジは1つ。管理者が最初に直すべきは、手が入ったことより打刻の欠け。
        var records = new[]
        {
            Record(TimeRecordType.ClockIn, At("09:00"), EntryMethod.ManualAdd),
        };

        var summary = AttendanceAggregator.Summarize(StaffId, WorkDate, records);

        Assert.True(summary.HasManualAdd);
        Assert.Equal(AttendanceStatus.NeedsReview, summary.Status);
    }

    [Fact]
    public void メモは最後に書かれたものを出す()
    {
        var records = new[]
        {
            Record(TimeRecordType.ClockIn, At("09:00"), EntryMethod.ManualAdd, "古いメモ"),
            Record(TimeRecordType.ClockOut, At("18:00"), EntryMethod.ManualEdit, "新しいメモ"),
        };

        var summary = AttendanceAggregator.Summarize(StaffId, WorkDate, records);

        Assert.Equal("新しいメモ", summary.Note);
    }

    [Fact]
    public void 退勤が出勤より前なら実労働を出さない()
    {
        // 手修正で「営業日 09/08・退勤 02:00」と入れると、
        // 打刻日時が出勤より前になることがある。
        var records = new[]
        {
            Record(TimeRecordType.ClockIn, At("12:37")),
            Record(TimeRecordType.ClockOut, At("02:00")),
        };

        var summary = AttendanceAggregator.Summarize(StaffId, WorkDate, records);

        // 0.0h と出すと「0時間働いた」ように見える。計算できないことを示す。
        Assert.Null(summary.WorkedMinutes);
        Assert.Equal(AttendanceStatus.NeedsReview, summary.Status);
    }

    [Fact]
    public void 中抜けが実労働を超えても負にしない()
    {
        // 打刻が壊れている場合。負の時間を一覧に出さない。
        var records = new[]
        {
            Record(TimeRecordType.ClockIn, At("09:00")),
            Record(TimeRecordType.BreakStart, At("09:10")),
            Record(TimeRecordType.BreakEnd, At("18:00")),
            Record(TimeRecordType.ClockOut, At("09:20")),
        };

        var summary = AttendanceAggregator.Summarize(StaffId, WorkDate, records);

        Assert.Equal(0, summary.WorkedMinutes);
    }

    [Fact]
    public void スタッフと営業日ごとに行を分ける()
    {
        var other = Guid.CreateVersion7();

        var records = new[]
        {
            Record(TimeRecordType.ClockIn, At("09:00")),
            Record(TimeRecordType.ClockOut, At("18:00")),
            Record(TimeRecordType.ClockIn, At("10:00"), staffId: other),
            Record(TimeRecordType.ClockOut, At("19:00"), staffId: other),
            Record(TimeRecordType.ClockIn, new DateTime(2026, 9, 5, 9, 0, 0), workDate: new DateOnly(2026, 9, 5)),
        };

        var rows = AttendanceAggregator.SummarizeAll(records);

        Assert.Equal(3, rows.Count);

        // 営業日の昇順。1日から順に並んでいるほうが月ぶんを追いやすい。
        Assert.Equal(new DateOnly(2026, 9, 5), rows[0].WorkDate);
        Assert.Equal(new DateOnly(2026, 9, 6), rows[^1].WorkDate);
    }

    // ---- 補助 ----

    private static DailyAttendance Summarize(params (TimeRecordType Type, string Time)[] punches)
        => AttendanceAggregator.Summarize(
            StaffId, WorkDate, punches.Select(p => Record(p.Type, At(p.Time))));

    private static DateTime At(string time)
        => WorkDate.ToDateTime(TimeOnly.Parse(time, System.Globalization.CultureInfo.InvariantCulture));

    private static TimeRecord Record(
        TimeRecordType type,
        DateTime recordedAt,
        EntryMethod method = EntryMethod.Qr,
        string? note = null,
        Guid? staffId = null,
        DateOnly? workDate = null) => new()
    {
        Id = Guid.CreateVersion7(),
        StoreId = Guid.CreateVersion7(),
        StaffId = staffId ?? StaffId,
        RecordType = type,
        RecordedAt = recordedAt,
        WorkDate = workDate ?? WorkDate,
        EntryMethod = method,
        Note = note,
    };

    [Fact]
    public void 同じ営業日は出勤の早い順に並ぶ()
    {
        var early = Guid.CreateVersion7();
        var late = Guid.CreateVersion7();

        var records = new[]
        {
            Record(TimeRecordType.ClockIn, At("13:00"), staffId: late),
            Record(TimeRecordType.ClockIn, At("09:00"), staffId: early),
        };

        var rows = AttendanceAggregator.SummarizeAll(records);

        Assert.Equal(early, rows[0].StaffId);
        Assert.Equal(late, rows[1].StaffId);
    }
}
