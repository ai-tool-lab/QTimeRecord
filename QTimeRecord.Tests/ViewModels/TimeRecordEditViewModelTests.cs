using QTimeRecord.App.Services;
using QTimeRecord.App.ViewModels;
using QTimeRecord.Core.Domain;
using QTimeRecord.Core.Services;

namespace QTimeRecord.Tests.ViewModels;

public sealed class TimeRecordEditViewModelTests
{
    private static readonly Guid StaffId = Guid.CreateVersion7();
    private static readonly DateOnly WorkDate = new(2026, 9, 6);

    /// <summary>
    /// 0時始まり＝営業日と暦日が一致する店舗。「翌日」の既定が働かないため、
    /// 日跨ぎに関係しないテストの期待値はこれまでどおりになる。
    /// </summary>
    private static readonly TimeOnly BusinessDayStart = new(0, 0);

    /// <summary>11時始まり。深夜まで開ける店舗の日跨ぎを確かめるために使う。</summary>
    private static readonly TimeOnly LateNightStart = new(11, 0);

    [Theory]
    [InlineData("", "__:__")]
    [InlineData("09", "09:__")]
    [InlineData("0930", "09:30")]
    public void 入力中の時刻は下線で桁を見せる(string digits, string expected)
    {
        var (viewModel, _, _) = Create();
        viewModel.OpenForAdd(StaffOptions, WorkDate, BusinessDayStart);

        viewModel.Time = digits;

        Assert.Equal(expected, viewModel.TimeText);
    }

    [Theory]
    [InlineData("0930", 9, 30)]
    [InlineData("0000", 0, 0)]
    [InlineData("2359", 23, 59)]
    public void 四桁がそろえば時刻になる(string digits, int hour, int minute)
    {
        var (viewModel, _, _) = Create();
        viewModel.OpenForAdd(StaffOptions, WorkDate, BusinessDayStart);

        viewModel.Time = digits;

        Assert.Equal(WorkDate.ToDateTime(new TimeOnly(hour, minute)), viewModel.ParseTime());
        Assert.True(viewModel.CanSave);
    }

    [Theory]
    [InlineData("2400")]
    [InlineData("0960")]
    [InlineData("9999")]
    [InlineData("093")]
    public void 範囲外や桁不足は保存できない(string digits)
    {
        var (viewModel, _, _) = Create();
        viewModel.OpenForAdd(StaffOptions, WorkDate, BusinessDayStart);

        viewModel.Time = digits;

        Assert.Null(viewModel.ParseTime());
        Assert.False(viewModel.CanSave);
    }

    [Fact]
    public void 営業日を前後に動かせる()
    {
        var (viewModel, _, _) = Create();
        viewModel.OpenForAdd(StaffOptions, WorkDate, BusinessDayStart);

        viewModel.PreviousDayCommand.Execute(null);
        Assert.Equal(new DateOnly(2026, 9, 5), viewModel.WorkDate);

        viewModel.NextDayCommand.Execute(null);
        viewModel.NextDayCommand.Execute(null);
        Assert.Equal(new DateOnly(2026, 9, 7), viewModel.WorkDate);
    }

    [Fact]
    public void 新規登録では削除できない()
    {
        var (viewModel, _, _) = Create();
        viewModel.OpenForAdd(StaffOptions, WorkDate, BusinessDayStart);

        Assert.False(viewModel.CanDelete);
        Assert.Equal("打刻を追加", viewModel.Title);
        Assert.True(viewModel.CanChooseStaff);
    }

    [Fact]
    public void 行から開くとその日の打刻が並ぶ()
    {
        var (viewModel, _, _) = Create();

        viewModel.OpenForRow(StaffOptions, RowWith(
            (TimeRecordType.ClockIn, "09:00"),
            (TimeRecordType.ClockOut, "18:00")), BusinessDayStart);

        // 打刻2件 ＋ 「新しい打刻を追加」。打刻漏れの補填はこの行から行うのが自然。
        Assert.Equal(3, viewModel.Punches.Count);
        Assert.Null(viewModel.Punches[^1].Record);
    }

    [Fact]
    public void 打刻を選ぶと内容が読み込まれる()
    {
        var (viewModel, _, _) = Create();

        viewModel.OpenForRow(StaffOptions, RowWith(
            (TimeRecordType.ClockIn, "09:00"),
            (TimeRecordType.ClockOut, "18:05")), BusinessDayStart);

        viewModel.SelectedPunch = viewModel.Punches[1];

        Assert.Equal("1805", viewModel.Time);
        Assert.Equal(TimeRecordType.ClockOut, viewModel.SelectedRecordType.Type);
        Assert.Equal("打刻を修正", viewModel.Title);
        Assert.True(viewModel.CanDelete);

        // 修正では対象を変えさせない。別人の勤怠に付け替わる。
        Assert.False(viewModel.CanChooseStaff);
    }

    [Fact]
    public void 追加を選ぶと入力が空に戻る()
    {
        var (viewModel, _, _) = Create();

        viewModel.OpenForRow(StaffOptions, RowWith((TimeRecordType.ClockIn, "09:00")), BusinessDayStart);
        Assert.Equal("0900", viewModel.Time);

        viewModel.SelectedPunch = viewModel.Punches[^1];

        Assert.Equal(string.Empty, viewModel.Time);
        Assert.True(viewModel.CanChooseStaff);
    }

    [Fact]
    public async Task 保存すると登録して完了を通知する()
    {
        var (viewModel, editor, _) = Create();
        viewModel.OpenForAdd(StaffOptions, WorkDate, BusinessDayStart);
        viewModel.Time = "0930";

        var completed = false;
        viewModel.Completed += (_, _) => completed = true;

        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)viewModel.SaveCommand)
            .ExecuteAsync(null);

        Assert.True(completed);
        var draft = Assert.Single(editor.Added);
        Assert.Equal(WorkDate.ToDateTime(new TimeOnly(9, 30)), draft.RecordedAt);
        Assert.Equal(WorkDate, draft.WorkDate);
    }

    [Fact]
    public async Task 未来の日時は確認を取ってから保存する()
    {
        var (viewModel, editor, dialogs) = Create();
        editor.Warning = "未来の日時です。このまま登録しますか？";
        dialogs.ConfirmResult = true;

        viewModel.OpenForAdd(StaffOptions, WorkDate, BusinessDayStart);
        viewModel.Time = "0930";

        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)viewModel.SaveCommand)
            .ExecuteAsync(null);

        Assert.True(dialogs.ConfirmAsked);
        Assert.Single(editor.Added);
    }

    [Fact]
    public async Task 確認をやめたら保存しない()
    {
        var (viewModel, editor, dialogs) = Create();
        editor.Warning = "未来の日時です。このまま登録しますか？";
        dialogs.ConfirmResult = false;

        viewModel.OpenForAdd(StaffOptions, WorkDate, BusinessDayStart);
        viewModel.Time = "0930";

        var completed = false;
        viewModel.Completed += (_, _) => completed = true;

        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)viewModel.SaveCommand)
            .ExecuteAsync(null);

        Assert.Empty(editor.Added);
        Assert.False(completed);
    }

    [Fact]
    public async Task 検証で弾かれたら理由を出して保存しない()
    {
        var (viewModel, editor, _) = Create();
        editor.Error = "スタッフが見つかりません。";

        viewModel.OpenForAdd(StaffOptions, WorkDate, BusinessDayStart);
        viewModel.Time = "0930";

        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)viewModel.SaveCommand)
            .ExecuteAsync(null);

        Assert.Equal("スタッフが見つかりません。", viewModel.ErrorMessage);
        Assert.Empty(editor.Added);
    }

    [Fact]
    public async Task 保存に失敗したら黙って閉じない()
    {
        var (viewModel, editor, _) = Create();
        editor.Throw = true;

        viewModel.OpenForAdd(StaffOptions, WorkDate, BusinessDayStart);
        viewModel.Time = "0930";

        var completed = false;
        viewModel.Completed += (_, _) => completed = true;

        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)viewModel.SaveCommand)
            .ExecuteAsync(null);

        // 閉じてしまうと、保存できなかったことに管理者が気づけない。
        Assert.NotNull(viewModel.ErrorMessage);
        Assert.False(completed);
    }

    [Fact]
    public async Task 削除は確認を取ってから行う()
    {
        var (viewModel, editor, dialogs) = Create();
        dialogs.ConfirmResult = true;

        viewModel.OpenForRow(StaffOptions, RowWith((TimeRecordType.ClockIn, "09:00")), BusinessDayStart);

        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)viewModel.DeleteCommand)
            .ExecuteAsync(null);

        Assert.True(dialogs.ConfirmAsked);
        Assert.Single(editor.Deleted);
    }

    [Fact]
    public async Task 削除の確認をやめたら消さない()
    {
        var (viewModel, editor, dialogs) = Create();
        dialogs.ConfirmResult = false;

        viewModel.OpenForRow(StaffOptions, RowWith((TimeRecordType.ClockIn, "09:00")), BusinessDayStart);

        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)viewModel.DeleteCommand)
            .ExecuteAsync(null);

        // 削除は取り消せない。確認を通らない経路を作らない。
        Assert.Empty(editor.Deleted);
    }

    [Fact]
    public void 全スタッフは登録先に出さない()
    {
        var (viewModel, _, _) = Create();

        viewModel.OpenForAdd([StaffOption.Everyone, .. StaffOptions], WorkDate, BusinessDayStart);

        // 「全スタッフ」に打刻を登録することはできない。
        Assert.DoesNotContain(viewModel.StaffOptions, o => o.Id is null);
    }

    [Fact]
    public void 打刻の選択肢に登録方法を出す()
    {
        var record = NewRecord(TimeRecordType.ClockIn, "09:00", EntryMethod.ManualEdit);

        // どれが手で直されたものか、選ぶ前に分かるようにする。
        Assert.Contains("手修正", TimeRecordEditViewModel.Describe(record), StringComparison.Ordinal);
        Assert.Contains("09:00", TimeRecordEditViewModel.Describe(record), StringComparison.Ordinal);
    }

    // ---- 補助 ----

    [Fact]
    public void 営業開始より前の時刻は翌日として扱う()
    {
        var (viewModel, _, _) = Create();
        viewModel.OpenForAdd(StaffOptions, WorkDate, LateNightStart);

        viewModel.Time = "0200";

        // 11時始まりの店では 02:00 の退勤は翌朝。当日 02:00 にすると出勤より前になる。
        Assert.True(viewModel.IsNextDay);
        Assert.Equal(WorkDate.AddDays(1).ToDateTime(new TimeOnly(2, 0)), viewModel.ParseTime());
    }

    [Fact]
    public void 営業開始以降の時刻は当日のまま()
    {
        var (viewModel, _, _) = Create();
        viewModel.OpenForAdd(StaffOptions, WorkDate, LateNightStart);

        viewModel.Time = "2200";

        Assert.False(viewModel.IsNextDay);
        Assert.Equal(WorkDate.ToDateTime(new TimeOnly(22, 0)), viewModel.ParseTime());
    }

    [Fact]
    public void 翌日を手で外したら自動判定に戻さない()
    {
        var (viewModel, _, _) = Create();
        viewModel.OpenForAdd(StaffOptions, WorkDate, LateNightStart);
        viewModel.Time = "0200";

        viewModel.ToggleNextDay(false);
        viewModel.Time = "0300";

        // 打ち直すたびに翌日へ戻ると、管理者の指定を上書きしてしまう。
        Assert.False(viewModel.IsNextDay);
        Assert.Equal(WorkDate.ToDateTime(new TimeOnly(3, 0)), viewModel.ParseTime());
    }

    [Fact]
    public void 翌日に記録された打刻は翌日のまま開く()
    {
        var (viewModel, _, _) = Create();
        var record = NewRecord(TimeRecordType.ClockOut, "02:00");
        record.RecordedAt = WorkDate.AddDays(1).ToDateTime(new TimeOnly(2, 0));

        var summary = AttendanceAggregator.Summarize(
            StaffId, WorkDate, [NewRecord(TimeRecordType.ClockIn, "22:00"), record]);

        viewModel.OpenForRow(
            StaffOptions,
            new AttendanceRow(summary, "山田 太郎", "E-01", "アルバイト"),
            LateNightStart);
        viewModel.SelectedPunch = viewModel.Punches.First(p => p.Record?.Id == record.Id);

        // 時刻だけ読み戻すと、開き直して保存しただけで前日へずれる。
        Assert.True(viewModel.IsNextDay);
        Assert.Equal(record.RecordedAt, viewModel.ParseTime());
    }

    [Fact]
    public void 打刻日時には暦日を出す()
    {
        var (viewModel, _, _) = Create();
        viewModel.OpenForAdd(StaffOptions, WorkDate, LateNightStart);

        Assert.Equal("—", viewModel.RecordedAtText);

        viewModel.Time = "0200";

        // 営業日と暦日がずれることを、保存前に管理者へ見せる。
        Assert.Equal("2026/09/07 02:00", viewModel.RecordedAtText);
    }

    private static IReadOnlyList<StaffOption> StaffOptions =>
    [
        new(StaffId, "山田 太郎"),
        new(Guid.CreateVersion7(), "高橋 美咲"),
    ];

    private static (TimeRecordEditViewModel ViewModel, StubEditService Editor, StubDialogs Dialogs)
        Create()
    {
        var editor = new StubEditService();
        var dialogs = new StubDialogs();

        return (new TimeRecordEditViewModel(editor, dialogs), editor, dialogs);
    }

    private static AttendanceRow RowWith(params (TimeRecordType Type, string Time)[] punches)
    {
        var records = punches.Select(p => NewRecord(p.Type, p.Time)).ToList();
        var summary = AttendanceAggregator.Summarize(StaffId, WorkDate, records);

        return new AttendanceRow(summary, "山田 太郎", "E-01", "アルバイト");
    }

    private static TimeRecord NewRecord(
        TimeRecordType type, string time, EntryMethod method = EntryMethod.Qr) => new()
    {
        Id = Guid.CreateVersion7(),
        StoreId = Guid.CreateVersion7(),
        StaffId = StaffId,
        RecordType = type,
        RecordedAt = WorkDate.ToDateTime(
            TimeOnly.Parse(time, System.Globalization.CultureInfo.InvariantCulture)),
        WorkDate = WorkDate,
        EntryMethod = method,
    };

    private sealed class StubEditService : ITimeRecordEditService
    {
        public string? Error { get; set; }

        public string? Warning { get; set; }

        public bool Throw { get; set; }

        public List<TimeRecordDraft> Added { get; } = [];

        public List<TimeRecordDraft> Updated { get; } = [];

        public List<Guid> Deleted { get; } = [];

        public Task<EditValidation> ValidateAsync(
            TimeRecordDraft draft, CancellationToken ct = default)
            => Task.FromResult(new EditValidation(Error, Warning));

        public Task<TimeRecord> AddAsync(TimeRecordDraft draft, CancellationToken ct = default)
        {
            if (Throw)
            {
                throw new InvalidOperationException("保存に失敗しました。");
            }

            Added.Add(draft);

            return Task.FromResult(NewRecord(draft.RecordType, "09:00"));
        }

        public Task<TimeRecord> UpdateAsync(TimeRecordDraft draft, CancellationToken ct = default)
        {
            if (Throw)
            {
                throw new InvalidOperationException("保存に失敗しました。");
            }

            Updated.Add(draft);

            return Task.FromResult(NewRecord(draft.RecordType, "09:00"));
        }

        public Task DeleteAsync(Guid recordId, CancellationToken ct = default)
        {
            Deleted.Add(recordId);

            return Task.CompletedTask;
        }
    }

    private sealed class StubDialogs : IDialogService
    {
        public bool ConfirmResult { get; set; }

        public bool ConfirmAsked { get; private set; }

        public bool Confirm(
            string title, string message, string okText = "OK", string cancelText = "キャンセル")
        {
            ConfirmAsked = true;
            return ConfirmResult;
        }

        public void ShowInfo(string title, string message)
        {
        }

        public void ShowError(string title, string message)
        {
        }
    }
}
