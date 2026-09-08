using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using QTimeRecord.App.Services;
using QTimeRecord.Core.Data.Repositories;
using QTimeRecord.Core.Domain;
using QTimeRecord.Core.Infrastructure;
using QTimeRecord.Core.Services;

namespace QTimeRecord.App.ViewModels;

/// <summary>スタッフ絞り込みの選択肢。全員を表す項目を先頭に置く。</summary>
/// <param name="Id">スタッフ ID。全員なら null。</param>
/// <param name="Name">表示名。</param>
public sealed record StaffOption(Guid? Id, string Name)
{
    /// <summary>絞り込みなし。一覧の既定。</summary>
    public static readonly StaffOption Everyone = new(null, "全スタッフ");
}

/// <summary>絞り込みの選択肢。</summary>
/// <param name="Filter">絞り込みの種類。</param>
/// <param name="Label">ボタンの文言。</param>
public sealed record FilterOption(AttendanceFilter Filter, string Label);

/// <summary>
/// 一覧の1行の表示。
///
/// 書式は ViewModel 側で作る。XAML の StringFormat に散らすと、
/// 「時刻が無い日は空欄」のような規則が画面ごとにばらける。
/// </summary>
public sealed record AttendanceRowView(AttendanceRow Source)
{
    private static readonly CultureInfo Japanese = new("ja-JP");

    public DateOnly WorkDate => Source.WorkDate;

    public string DateText => Source.WorkDate.ToString("MM/dd", Japanese);

    public string DayOfWeekText
        => $"（{Japanese.DateTimeFormat.GetShortestDayName(Source.WorkDate.DayOfWeek)}）";

    /// <summary>土日は色を変える（→ plan.md 11-1）。</summary>
    public bool IsWeekend => Source.WorkDate.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

    public bool IsSunday => Source.WorkDate.DayOfWeek == DayOfWeek.Sunday;

    public string StaffName => Source.StaffName;

    public string? EmploymentType => Source.EmploymentType;

    public string ClockInText => Time(Source.Summary.ClockInAt);

    public string ClockOutText => Time(Source.Summary.ClockOutAt);

    /// <summary>「01:00（2回）」。回数を添えないと、複数回あったことが読み取れない。</summary>
    public string BreakText => Source.Summary.BreakCount switch
    {
        0 => "—",
        1 => Duration(Source.Summary.BreakMinutes),
        var count => $"{Duration(Source.Summary.BreakMinutes)}（{count}回）",
    };

    public string WorkedText => Source.Summary.WorkedMinutes is { } minutes
        ? $"{minutes / 60.0:0.0}h"
        : "—";

    /// <summary>
    /// 要確認の理由。要確認でなければ null。
    ///
    /// 一覧には最初の出勤と最後の退勤しか出ないため、バッジだけでは
    /// どの打刻が原因なのか分からない。理由は編集ダイアログにも同じものを出す。
    /// </summary>
    public string? ReviewReasonText => Source.Summary.ReviewReasons.Count > 0
        ? string.Join(Environment.NewLine, Source.Summary.ReviewReasons)
        : null;

    public string StatusText => Source.Status switch
    {
        AttendanceStatus.NeedsReview => "要確認",
        AttendanceStatus.ManualAdd => "手入力",
        AttendanceStatus.ManualEdit => "手修正",
        _ => "通常",
    };

    public AttendanceStatus Status => Source.Status;

    public string? Note => Source.Summary.Note;

    /// <summary>時刻。無い場合は空欄ではなく記号を出す。空欄は「まだ読み込み中」に見える。</summary>
    private static string Time(DateTime? value)
        => value is { } at ? at.ToString("HH:mm", Japanese) : "—";

    private static string Duration(int minutes) => $"{minutes / 60:00}:{minutes % 60:00}";
}

/// <summary>
/// 勤務状況の一覧。管理画面の最初のタブ。
/// </summary>
public sealed partial class AttendanceViewModel : ObservableObject
{
    private readonly IAttendanceQueryService _query;
    private readonly IStaffRepository _staff;
    private readonly IStoreRepository _stores;
    private readonly IAttendanceEditor _editor;
    private readonly ICsvExportService _csv;
    private readonly IFileDialogService _files;
    private readonly IDialogService _dialogs;
    private readonly AppPaths _paths;
    private readonly IClock _clock;
    private readonly ILogger<AttendanceViewModel> _logger;

    public AttendanceViewModel(
        IAttendanceQueryService query,
        IStaffRepository staff,
        IStoreRepository stores,
        IAttendanceEditor editor,
        ICsvExportService csv,
        IFileDialogService files,
        IDialogService dialogs,
        AppPaths paths,
        IClock clock,
        ILogger<AttendanceViewModel> logger)
    {
        _query = query;
        _staff = staff;
        _stores = stores;
        _editor = editor;
        _csv = csv;
        _files = files;
        _dialogs = dialogs;
        _paths = paths;
        _clock = clock;
        _logger = logger;

        _month = new DateOnly(clock.Today.Year, clock.Today.Month, 1);
        _selectedStaff = StaffOption.Everyone;
        _selectedFilter = Filters[0];

        PreviousMonthCommand = new AsyncRelayCommand(() => MoveMonthAsync(-1));
        NextMonthCommand = new AsyncRelayCommand(() => MoveMonthAsync(1));
        ThisMonthCommand = new AsyncRelayCommand(GoToThisMonthAsync);
        ReloadCommand = new AsyncRelayCommand(() => ReloadAsync());
        AddCommand = new AsyncRelayCommand(AddAsync);
        EditCommand = new AsyncRelayCommand<AttendanceRowView?>(EditAsync);
        ToggleSortCommand = new AsyncRelayCommand(ToggleSortAsync);
        ExportCsvCommand = new AsyncRelayCommand(ExportCsvAsync, () => !IsExporting);
        OpenExportFolderCommand = new RelayCommand(OpenExportFolder, () => ExportedPath is not null);
    }

    public static IReadOnlyList<FilterOption> Filters { get; } =
    [
        new(AttendanceFilter.All, "すべて"),
        new(AttendanceFilter.NeedsReview, "要確認"),
        new(AttendanceFilter.Incomplete, "打刻漏れ"),
    ];

    public ObservableCollection<AttendanceRowView> Rows { get; } = [];

    public ObservableCollection<StaffOption> StaffOptions { get; } = [];

    [ObservableProperty]
    private DateOnly _month;

    [ObservableProperty]
    private StaffOption _selectedStaff;

    [ObservableProperty]
    private FilterOption _selectedFilter;

    /// <summary>
    /// 新しい日付を上に出すか。既定は true。
    ///
    /// 開いた直後に見たいのは直近の勤怠。月ぶんを通して確認するときに切り替える。
    /// </summary>
    [ObservableProperty]
    private bool _newestFirst = true;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>出力中。二重実行を防ぐ。</summary>
    [ObservableProperty]
    private bool _isExporting;

    /// <summary>直前に出力した CSV のパス。まだ出していなければ null。</summary>
    [ObservableProperty]
    private string? _exportedPath;

    public string MonthLabel => $"{Month.Year}年{Month.Month}月";

    public bool IsEmpty => Rows.Count == 0 && !IsBusy;

    /// <summary>「全 24 件」。絞り込みの効き具合が分かるように出す。</summary>
    public string CountLabel => $"全 {Rows.Count} 件";

    /// <summary>
    /// 並び替えボタンの文言。
    ///
    /// <b>いまの並びを表す。</b>「押すとどうなるか」にすると、
    /// 表示中の順序がどちらなのか読み取れなくなる。
    /// </summary>
    public string SortLabel => NewestFirst ? "新しい日付順 ▼" : "古い日付順 ▲";

    public ICommand PreviousMonthCommand { get; }

    public ICommand NextMonthCommand { get; }

    public ICommand ThisMonthCommand { get; }

    public ICommand ReloadCommand { get; }

    public ICommand ToggleSortCommand { get; }

    public ICommand AddCommand { get; }

    public ICommand EditCommand { get; }

    public IRelayCommand ExportCsvCommand { get; }

    public IRelayCommand OpenExportFolderCommand { get; }

    /// <summary>スタッフ一覧と当月の勤怠を読み込む。</summary>
    public async Task LoadAsync(CancellationToken ct = default)
    {
        await LoadStaffOptionsAsync(ct);
        await ReloadAsync(ct);
    }

    /// <summary>一覧を読み込み直す。登録・修正・削除のあとは必ず通す。</summary>
    public async Task ReloadAsync(CancellationToken ct = default)
    {
        IsBusy = true;
        ErrorMessage = null;

        try
        {
            var rows = await _query.GetAsync(CurrentQuery(), ct);

            Rows.Clear();

            foreach (var row in rows)
            {
                Rows.Add(new AttendanceRowView(row));
            }
        }
        catch (Exception ex)
        {
            // 一覧が空なのか読めなかったのかを区別できないと、
            // 管理者は「打刻が消えた」と受け取る。
            _logger.LogError(ex, "勤務状況を読み込めませんでした。");
            ErrorMessage = "勤務状況を読み込めませんでした。しばらくしてからやり直してください。";
        }
        finally
        {
            IsBusy = false;

            OnPropertyChanged(nameof(CountLabel));
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    private async Task LoadStaffOptionsAsync(CancellationToken ct)
    {
        StaffOptions.Clear();
        StaffOptions.Add(StaffOption.Everyone);

        var store = await _stores.GetAsync(ct);

        if (store is null)
        {
            return;
        }

        // 退職者も含める。過去の月を見るときに絞り込めなくなるため。
        foreach (var member in await _staff.ListAsync(store.Id, status: null, search: null, ct))
        {
            StaffOptions.Add(new StaffOption(member.Id, member.Name));
        }

        SelectedStaff = StaffOptions[0];
    }

    private async Task MoveMonthAsync(int offset)
    {
        Month = Month.AddMonths(offset);
        await ReloadAsync();
    }

    private async Task GoToThisMonthAsync()
    {
        var today = _clock.Today;

        Month = new DateOnly(today.Year, today.Month, 1);
        await ReloadAsync();
    }

    private async Task AddAsync()
    {
        // 追加は表示中の月の1日を既定にする。今日が別の月なら、そちらへ入れたいことは少ない。
        if (await _editor.AddAsync([.. StaffOptions], Month))
        {
            await ReloadAsync();
        }
    }

    /// <summary>CSV を出力する。テストから直接呼べるよう公開する。</summary>
    public async Task ExportCsvAsync()
    {
        if (IsExporting)
        {
            return;
        }

        // 画面の表示と同じ条件で出す。別の条件だと、確認した内容と
        // 給与へ渡す内容が食い違う（→ plan.md 14-1）。
        var query = CurrentQuery();

        IsExporting = true;
        ExportedPath = null;

        try
        {
            var path = _files.AskSavePath(
                "CSVの保存先",
                await _csv.SuggestFileNameAsync(query),
                "CSV ファイル (*.csv)|*.csv",
                _paths.DefaultExportDirectory);

            if (path is null)
            {
                return;
            }

            var bytes = await _csv.ExportAsync(query);

            await File.WriteAllBytesAsync(path, bytes);

            ExportedPath = path;
        }
        catch (Exception ex)
        {
            // 保存できていないのに成功に見せると、給与の締めで初めて気づく。
            _logger.LogError(ex, "CSV を出力できませんでした。");
            // 画面には対処が分かる短文だけを出す。原因はログに残っている（→ plan.md 15-1）。
            _dialogs.ShowError("CSVを保存できませんでした", "保存先を確認してください。");
        }
        finally
        {
            IsExporting = false;
        }
    }

    private void OpenExportFolder()
    {
        if (ExportedPath is { } path)
        {
            _files.RevealInFolder(path);
        }
    }

    private AttendanceQuery CurrentQuery() => new()
    {
        Year = Month.Year,
        Month = Month.Month,
        StaffId = SelectedStaff.Id,
        Filter = SelectedFilter.Filter,
        Sort = NewestFirst ? AttendanceSort.DateDescending : AttendanceSort.DateAscending,
    };

    private async Task ToggleSortAsync()
    {
        NewestFirst = !NewestFirst;

        await ReloadAsync();
    }

    private async Task EditAsync(AttendanceRowView? row)
    {
        if (row is null)
        {
            return;
        }

        if (await _editor.EditAsync([.. StaffOptions], row.Source))
        {
            await ReloadAsync();
        }
    }

    partial void OnMonthChanged(DateOnly value) => OnPropertyChanged(nameof(MonthLabel));

    partial void OnSelectedStaffChanged(StaffOption value) => _ = ReloadAsync();

    partial void OnSelectedFilterChanged(FilterOption value) => _ = ReloadAsync();

    partial void OnNewestFirstChanged(bool value) => OnPropertyChanged(nameof(SortLabel));

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsEmpty));

    partial void OnIsExportingChanged(bool value) => ExportCsvCommand.NotifyCanExecuteChanged();

    partial void OnExportedPathChanged(string? value)
        => OpenExportFolderCommand.NotifyCanExecuteChanged();
}
