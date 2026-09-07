using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using QTimeRecord.App.Services;
using QTimeRecord.App.ViewModels;
using QTimeRecord.Core.Data.Repositories;
using QTimeRecord.Core.Domain;
using QTimeRecord.Core.Infrastructure;
using QTimeRecord.Core.Services;

namespace QTimeRecord.Tests.ViewModels;

public sealed class AttendanceViewModelTests
{
    [Fact]
    public async Task 起動時は今月を表示する()
    {
        var harness = new Harness(new DateTime(2026, 9, 6, 12, 0, 0));

        var viewModel = harness.Create();
        await viewModel.LoadAsync();

        Assert.Equal("2026年9月", viewModel.MonthLabel);
        Assert.Equal(2026, harness.Query.LastQuery!.Year);
        Assert.Equal(9, harness.Query.LastQuery.Month);
    }

    [Fact]
    public async Task 前月と翌月へ移動できる()
    {
        var harness = new Harness(new DateTime(2026, 1, 15, 12, 0, 0));

        var viewModel = harness.Create();
        await viewModel.LoadAsync();

        viewModel.PreviousMonthCommand.Execute(null);
        Assert.Equal("2025年12月", viewModel.MonthLabel);

        viewModel.NextMonthCommand.Execute(null);
        viewModel.NextMonthCommand.Execute(null);
        Assert.Equal("2026年2月", viewModel.MonthLabel);
    }

    [Fact]
    public async Task 今月ボタンで戻れる()
    {
        var harness = new Harness(new DateTime(2026, 9, 6, 12, 0, 0));

        var viewModel = harness.Create();
        await viewModel.LoadAsync();

        viewModel.PreviousMonthCommand.Execute(null);
        viewModel.PreviousMonthCommand.Execute(null);
        viewModel.ThisMonthCommand.Execute(null);

        Assert.Equal("2026年9月", viewModel.MonthLabel);
    }

    [Fact]
    public async Task スタッフの選択肢に全員が入る()
    {
        var harness = new Harness();

        var viewModel = harness.Create();
        await viewModel.LoadAsync();

        Assert.Equal("全スタッフ", viewModel.StaffOptions[0].Name);
        Assert.Null(viewModel.StaffOptions[0].Id);
        Assert.Equal(3, viewModel.StaffOptions.Count);
    }

    [Fact]
    public async Task スタッフを選ぶと絞り込んで読み直す()
    {
        var harness = new Harness();

        var viewModel = harness.Create();
        await viewModel.LoadAsync();

        viewModel.SelectedStaff = viewModel.StaffOptions[1];

        Assert.Equal(viewModel.StaffOptions[1].Id, harness.Query.LastQuery!.StaffId);
    }

    [Fact]
    public async Task 絞り込みを変えると読み直す()
    {
        var harness = new Harness();

        var viewModel = harness.Create();
        await viewModel.LoadAsync();

        viewModel.SelectedFilter = AttendanceViewModel.Filters[1];

        Assert.Equal(AttendanceFilter.NeedsReview, harness.Query.LastQuery!.Filter);
    }

    [Fact]
    public async Task 読み込みに失敗したら空一覧と区別できる()
    {
        var harness = new Harness();
        harness.Query.Throw = true;

        var viewModel = harness.Create();
        await viewModel.LoadAsync();

        // 区別できないと、管理者は「打刻が消えた」と受け取る。
        Assert.NotNull(viewModel.ErrorMessage);
        Assert.Empty(viewModel.Rows);
    }

    [Fact]
    public async Task 編集を保存したら一覧を読み直す()
    {
        var harness = new Harness();
        harness.Editor.Result = true;

        var viewModel = harness.Create();
        await viewModel.LoadAsync();

        var before = harness.Query.Calls;

        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)viewModel.AddCommand)
            .ExecuteAsync(null);

        // 読み直さないと、登録したのに一覧に出ない。
        Assert.Equal(before + 1, harness.Query.Calls);
    }

    [Fact]
    public async Task 編集をやめたら読み直さない()
    {
        var harness = new Harness();
        harness.Editor.Result = false;

        var viewModel = harness.Create();
        await viewModel.LoadAsync();

        var before = harness.Query.Calls;

        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)viewModel.AddCommand)
            .ExecuteAsync(null);

        Assert.Equal(before, harness.Query.Calls);
    }

    // ---- 並び順 ----

    [Fact]
    public async Task 既定は新しい日付順()
    {
        var harness = new Harness();

        var viewModel = harness.Create();
        await viewModel.LoadAsync();

        Assert.True(viewModel.NewestFirst);
        Assert.Equal(AttendanceSort.DateDescending, harness.Query.LastQuery!.Sort);
        Assert.Contains("新しい", viewModel.SortLabel, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 並び順を切り替えると読み直す()
    {
        var harness = new Harness();

        var viewModel = harness.Create();
        await viewModel.LoadAsync();

        var before = harness.Query.Calls;

        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)viewModel.ToggleSortCommand)
            .ExecuteAsync(null);

        Assert.False(viewModel.NewestFirst);
        Assert.Equal(AttendanceSort.DateAscending, harness.Query.LastQuery!.Sort);
        Assert.Equal(before + 1, harness.Query.Calls);

        // 文言はいまの並びを表す。押した先を表すと、どちらの順か読み取れない。
        Assert.Contains("古い", viewModel.SortLabel, StringComparison.Ordinal);
    }

    [Fact]
    public async Task もう一度押すと元の並びに戻る()
    {
        var harness = new Harness();

        var viewModel = harness.Create();
        await viewModel.LoadAsync();

        var toggle = (CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)viewModel.ToggleSortCommand;

        await toggle.ExecuteAsync(null);
        await toggle.ExecuteAsync(null);

        Assert.True(viewModel.NewestFirst);
        Assert.Equal(AttendanceSort.DateDescending, harness.Query.LastQuery!.Sort);
    }

    [Fact]
    public async Task CSV出力も画面の並びに従う()
    {
        var harness = new Harness();
        harness.Files.Path = System.IO.Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.csv");

        var viewModel = harness.Create();
        await viewModel.LoadAsync();

        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)viewModel.ToggleSortCommand)
            .ExecuteAsync(null);

        await viewModel.ExportCsvAsync();

        // 画面と CSV で並びが違うと、突き合わせるときに読み違える。
        Assert.Equal(AttendanceSort.DateAscending, harness.Csv.LastQuery!.Sort);

        File.Delete(harness.Files.Path);
    }

    // ---- CSV出力 ----

    [Fact]
    public async Task 出力は画面の絞り込みと同じ条件で行う()
    {
        var harness = new Harness();
        harness.Files.Path = System.IO.Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.csv");

        var viewModel = harness.Create();
        await viewModel.LoadAsync();

        viewModel.SelectedFilter = AttendanceViewModel.Filters[1];
        viewModel.SelectedStaff = viewModel.StaffOptions[1];

        await viewModel.ExportCsvAsync();

        // 別の条件で出すと、画面で確認した内容と給与へ渡す内容が食い違う。
        Assert.NotNull(harness.Csv.LastQuery);
        Assert.Equal(AttendanceFilter.NeedsReview, harness.Csv.LastQuery.Filter);
        Assert.Equal(viewModel.StaffOptions[1].Id, harness.Csv.LastQuery.StaffId);

        File.Delete(harness.Files.Path);
    }

    [Fact]
    public async Task 保存先を選ばなければ書き出さない()
    {
        var harness = new Harness();
        harness.Files.Path = null;

        var viewModel = harness.Create();
        await viewModel.LoadAsync();

        await viewModel.ExportCsvAsync();

        Assert.Null(viewModel.ExportedPath);
        Assert.False(viewModel.OpenExportFolderCommand.CanExecute(null));
    }

    [Fact]
    public async Task 出力するとファイルが書かれ保存先を開ける()
    {
        var harness = new Harness();
        var path = System.IO.Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.csv");
        harness.Files.Path = path;

        var viewModel = harness.Create();
        await viewModel.LoadAsync();

        await viewModel.ExportCsvAsync();

        Assert.True(File.Exists(path));
        Assert.Equal(path, viewModel.ExportedPath);

        // 「どこに保存されたか分からない」という問い合わせを減らす。
        Assert.True(viewModel.OpenExportFolderCommand.CanExecute(null));

        viewModel.OpenExportFolderCommand.Execute(null);
        Assert.Equal(1, harness.Files.RevealCalls);

        File.Delete(path);
    }

    [Fact]
    public async Task 出力に失敗したら成功として見せない()
    {
        var harness = new Harness();
        harness.Files.Path = System.IO.Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.csv");
        harness.Csv.Throw = true;

        var viewModel = harness.Create();
        await viewModel.LoadAsync();

        await viewModel.ExportCsvAsync();

        // 保存できていないのに成功に見せると、給与の締めで初めて気づく。
        Assert.True(harness.Dialogs.ErrorShown);
        Assert.Null(viewModel.ExportedPath);
    }

    [Fact]
    public async Task 書き込めない保存先ならエラーにする()
    {
        var harness = new Harness();

        // 存在しないフォルダ。書き込み不可のときと同じ経路を通る。
        harness.Files.Path = System.IO.Path.Combine(
            Path.GetTempPath(), Guid.NewGuid().ToString("N"), "sub", "out.csv");

        var viewModel = harness.Create();
        await viewModel.LoadAsync();

        await viewModel.ExportCsvAsync();

        Assert.True(harness.Dialogs.ErrorShown);
        Assert.Null(viewModel.ExportedPath);
    }

    [Fact]
    public async Task 出力中は二重に実行しない()
    {
        var harness = new Harness();
        harness.Files.Path = System.IO.Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.csv");

        var viewModel = harness.Create();
        await viewModel.LoadAsync();

        viewModel.IsExporting = true;

        await viewModel.ExportCsvAsync();

        Assert.False(viewModel.ExportCsvCommand.CanExecute(null));
        Assert.Null(harness.Csv.LastQuery);
    }

    // ---- 行の表示 ----

    [Fact]
    public void 時刻が無い日は記号を出す()
    {
        var row = Row(clockIn: "09:00", clockOut: null);

        // 空欄は「まだ読み込み中」に見える。
        Assert.Equal("09:00", row.ClockInText);
        Assert.Equal("—", row.ClockOutText);
        Assert.Equal("—", row.WorkedText);
    }

    [Fact]
    public void 中抜けは合計と回数を出す()
    {
        var row = Row(clockIn: "09:00", clockOut: "18:00", breaks: [("12:00", "12:30"), ("15:00", "15:15")]);

        // 回数を添えないと、複数回あったことが読み取れない。
        Assert.Equal("00:45（2回）", row.BreakText);
        Assert.Equal("8.3h", row.WorkedText);
    }

    [Fact]
    public void 一回だけの中抜けに回数は付けない()
    {
        var row = Row(clockIn: "09:00", clockOut: "18:00", breaks: [("12:00", "13:00")]);

        Assert.Equal("01:00", row.BreakText);
    }

    [Theory]
    [InlineData("2026-09-07", false, false)]
    [InlineData("2026-09-05", true, false)]
    [InlineData("2026-09-06", true, true)]
    public void 土日を見分ける(string date, bool weekend, bool sunday)
    {
        var row = Row(clockIn: "09:00", clockOut: "18:00", workDate: DateOnly.Parse(date,
            System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(weekend, row.IsWeekend);
        Assert.Equal(sunday, row.IsSunday);
    }

    // ---- 補助 ----

    private static AttendanceRowView Row(
        string? clockIn,
        string? clockOut,
        (string Start, string End)[]? breaks = null,
        DateOnly? workDate = null)
    {
        var date = workDate ?? new DateOnly(2026, 9, 7);
        var records = new List<TimeRecord>();

        void Add(TimeRecordType type, string time) => records.Add(new TimeRecord
        {
            Id = Guid.CreateVersion7(),
            StoreId = Guid.CreateVersion7(),
            StaffId = Guid.CreateVersion7(),
            RecordType = type,
            RecordedAt = date.ToDateTime(
                TimeOnly.Parse(time, System.Globalization.CultureInfo.InvariantCulture)),
            WorkDate = date,
            EntryMethod = EntryMethod.Qr,
        });

        if (clockIn is not null)
        {
            Add(TimeRecordType.ClockIn, clockIn);
        }

        foreach (var (start, end) in breaks ?? [])
        {
            Add(TimeRecordType.BreakStart, start);
            Add(TimeRecordType.BreakEnd, end);
        }

        if (clockOut is not null)
        {
            Add(TimeRecordType.ClockOut, clockOut);
        }

        var summary = AttendanceAggregator.Summarize(records[0].StaffId, date, records);

        return new AttendanceRowView(new AttendanceRow(summary, "山田 太郎", "E-01", "アルバイト"));
    }

    private sealed class Harness(DateTime? now = null)
    {
        public StubQueryService Query { get; } = new();

        public StubEditor Editor { get; } = new();

        public TestClock Clock { get; } = new(now ?? new DateTime(2026, 9, 6, 12, 0, 0));

        public StubCsvExportService Csv { get; } = new();

        public StubFileDialogs Files { get; } = new();

        public StubDialogs Dialogs { get; } = new();

        public AttendanceViewModel Create() => new(
            Query,
            new StubStaffRepository(),
            new StubStoreRepository(),
            Editor,
            Csv,
            Files,
            Dialogs,
            new AppPaths(new AppSettings()),
            Clock,
            NullLogger<AttendanceViewModel>.Instance);
    }

    private sealed class StubCsvExportService : ICsvExportService
    {
        public AttendanceQuery? LastQuery { get; private set; }

        public bool Throw { get; set; }

        public Task<byte[]> ExportAsync(AttendanceQuery query, CancellationToken ct = default)
        {
            LastQuery = query;

            return Throw
                ? throw new InvalidOperationException("出力に失敗しました。")
                : Task.FromResult<byte[]>([1, 2, 3]);
        }

        public Task<string> SuggestFileNameAsync(
            AttendanceQuery query, CancellationToken ct = default)
            => Task.FromResult("QTimeRecord_勤務状況_202609.csv");
    }

    private sealed class StubFileDialogs : IFileDialogService
    {
        public string? Path { get; set; }

        public int RevealCalls { get; private set; }

        public string? AskSavePath(
            string title, string suggestedFileName, string filter, string? initialDirectory)
            => Path;

        public void RevealInFolder(string path) => RevealCalls++;
    }

    private sealed class StubDialogs : IDialogService
    {
        public bool ErrorShown { get; private set; }

        public bool Confirm(
            string title, string message, string okText = "OK", string cancelText = "キャンセル")
            => true;

        public void ShowInfo(string title, string message)
        {
        }

        public void ShowError(string title, string message) => ErrorShown = true;
    }

    private sealed class StubQueryService : IAttendanceQueryService
    {
        public AttendanceQuery? LastQuery { get; private set; }

        public int Calls { get; private set; }

        public bool Throw { get; set; }

        public Task<IReadOnlyList<AttendanceRow>> GetAsync(
            AttendanceQuery query, CancellationToken ct = default)
        {
            LastQuery = query;
            Calls++;

            return Throw
                ? throw new InvalidOperationException("読み込みに失敗しました。")
                : Task.FromResult<IReadOnlyList<AttendanceRow>>([]);
        }
    }

    private sealed class StubEditor : IAttendanceEditor
    {
        public bool Result { get; set; }

        public Task<bool> AddAsync(IReadOnlyList<StaffOption> staff, DateOnly workDate)
            => Task.FromResult(Result);

        public Task<bool> EditAsync(IReadOnlyList<StaffOption> staff, AttendanceRow row)
            => Task.FromResult(Result);
    }

    private sealed class StubStaffRepository : IStaffRepository
    {
        public Task<Staff?> GetByIdAsync(Guid staffId, CancellationToken ct = default)
            => Task.FromResult<Staff?>(null);

        public Task<IReadOnlyList<Staff>> ListAsync(
            Guid storeId, StaffStatus? status = null, string? search = null,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Staff>>(
            [
                New(storeId, "山田 太郎"),
                New(storeId, "高橋 美咲"),
            ]);

        public Task AddAsync(Staff staff, CancellationToken ct = default) => Task.CompletedTask;

        public Task UpdateAsync(Staff staff, CancellationToken ct = default) => Task.CompletedTask;

        public Task<bool> StaffNoExistsAsync(
            Guid storeId, string staffNo, Guid? excludeStaffId = null, CancellationToken ct = default)
            => Task.FromResult(false);

        private static Staff New(Guid storeId, string name) => new()
        {
            Id = Guid.CreateVersion7(),
            StoreId = storeId,
            Name = name,
            Status = StaffStatus.Active,
        };
    }

    private sealed class StubStoreRepository : IStoreRepository
    {
        public Task<Store?> GetAsync(CancellationToken ct = default) => Task.FromResult<Store?>(new Store
        {
            Id = Guid.CreateVersion7(),
            CompanyName = "テスト株式会社",
            StoreName = "相模原店",
            BusinessDayStart = new TimeOnly(9, 0),
            BusinessDayEnd = new TimeOnly(22, 0),
        });

        public Task<bool> ExistsAsync(CancellationToken ct = default) => Task.FromResult(true);

        public Task AddAsync(Store store, CancellationToken ct = default) => Task.CompletedTask;

        public Task UpdateAsync(Store store, CancellationToken ct = default) => Task.CompletedTask;

        public Task<IReadOnlyList<Announcement>> GetAnnouncementsAsync(
            Guid storeId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Announcement>>([]);

        public Task ReplaceAnnouncementsAsync(
            Guid storeId, IReadOnlyList<Announcement> announcements, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
