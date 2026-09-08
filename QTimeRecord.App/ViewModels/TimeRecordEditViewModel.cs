using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QTimeRecord.App.Services;
using QTimeRecord.Core.Domain;
using QTimeRecord.Core.Services;

namespace QTimeRecord.App.ViewModels;

/// <summary>ダイアログ内で選ぶ打刻1件。</summary>
/// <param name="Record">対象の打刻。新規追加なら null。</param>
/// <param name="Label">選択肢の文言。</param>
public sealed record PunchOption(TimeRecord? Record, string Label)
{
    /// <summary>新しい打刻を足す選択肢。</summary>
    public static readonly PunchOption New = new(null, "＋ 新しい打刻を追加");
}

/// <summary>打刻種別の選択肢。</summary>
/// <param name="Type">種別。</param>
/// <param name="Label">表示名。</param>
public sealed record RecordTypeOption(TimeRecordType Type, string Label);

/// <summary>
/// 打刻の手動登録・修正ダイアログ。
///
/// 時刻はテンキーで4桁（HHMM）入力する。
/// <b>文字入力にすると「25:70」のような値を受け取ってから弾くことになる。</b>
/// 桁で区切って組み立てれば、そもそも入力できる範囲を絞れる。
/// </summary>
public sealed partial class TimeRecordEditViewModel : ObservableObject
{
    /// <summary>時刻の桁数。HHMM。</summary>
    public const int TimeDigits = 4;

    private static readonly CultureInfo Japanese = new("ja-JP");

    private readonly ITimeRecordEditService _editor;
    private readonly IDialogService _dialogs;

    /// <summary>店舗の1日の開始時刻。翌日かどうかの既定を決めるのに使う。</summary>
    private TimeOnly _businessDayStart;

    /// <summary>利用者が「翌日」を手で切り替えたか。切り替えたら自動判定で上書きしない。</summary>
    private bool _nextDayTouched;

    public TimeRecordEditViewModel(ITimeRecordEditService editor, IDialogService dialogs)
    {
        _editor = editor;
        _dialogs = dialogs;

        _selectedPunch = PunchOption.New;
        _selectedRecordType = RecordTypes[0];

        SaveCommand = new AsyncRelayCommand(SaveAsync, () => CanSave);
        DeleteCommand = new AsyncRelayCommand(DeleteAsync, () => CanDelete);
        PreviousDayCommand = new RelayCommand(() => WorkDate = WorkDate.AddDays(-1));
        NextDayCommand = new RelayCommand(() => WorkDate = WorkDate.AddDays(1));
    }

    /// <summary>保存または削除が完了した。ダイアログを閉じて一覧を読み込み直す。</summary>
    public event EventHandler? Completed;

    public static IReadOnlyList<RecordTypeOption> RecordTypes { get; } =
    [
        new(TimeRecordType.ClockIn, "出勤"),
        new(TimeRecordType.ClockOut, "退勤"),
        new(TimeRecordType.BreakStart, "中抜け開始"),
        new(TimeRecordType.BreakEnd, "中抜け終了"),
    ];

    /// <summary>その日の打刻。行から開いたときだけ中身が入る。</summary>
    public ObservableCollection<PunchOption> Punches { get; } = [];

    public ObservableCollection<StaffOption> StaffOptions { get; } = [];

    [ObservableProperty]
    private StaffOption? _selectedStaff;

    [ObservableProperty]
    private PunchOption _selectedPunch;

    [ObservableProperty]
    private RecordTypeOption _selectedRecordType;

    [ObservableProperty]
    private DateOnly _workDate;

    /// <summary>
    /// 打刻日時が営業日の翌日か。
    ///
    /// <b>営業日と打刻の暦日は一致しない。</b>開始 09:00 の店舗で営業日 09/08 の退勤が
    /// 02:00 なら、実際の打刻は 09/09 02:00。これを表現できないと、
    /// 退勤が出勤より前になり実労働が計算できなくなる。
    /// </summary>
    [ObservableProperty]
    private bool _isNextDay;

    /// <summary>テンキーが書き込む4桁。「0930」。</summary>
    [ObservableProperty]
    private string _time = string.Empty;

    [ObservableProperty]
    private string _note = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>スタッフを選べるか。修正では対象を変えさせない。</summary>
    public bool CanChooseStaff => SelectedPunch.Record is null;

    /// <summary>この行が要確認になっている理由。無ければ null。</summary>
    [ObservableProperty]
    private string? _reviewReasonText;

    /// <summary>
    /// 実際に保存される打刻日時。
    ///
    /// 営業日と暦日がずれることがあるため、<b>保存される値をそのまま見せる。</b>
    /// 見せないと、翌日ぶんのつもりで当日に入れてしまう。
    /// </summary>
    public string RecordedAtText => ParseTime() is { } at
        ? at.ToString("yyyy/MM/dd HH:mm", Japanese)
        : "—";

    public string WorkDateText
        => $"{WorkDate:yyyy年M月d日}（{Japanese.DateTimeFormat.GetShortestDayName(WorkDate.DayOfWeek)}）";

    /// <summary>入力中の時刻。桁が足りないところは下線で見せる。</summary>
    public string TimeText
    {
        get
        {
            var padded = Time.PadRight(TimeDigits, '_');

            return $"{padded[..2]}:{padded[2..]}";
        }
    }

    public bool CanSave => !IsBusy && SelectedStaff?.Id is not null && ParseTime() is not null;

    public bool CanDelete => !IsBusy && SelectedPunch.Record is not null;

    public string Title => SelectedPunch.Record is null ? "打刻を追加" : "打刻を修正";

    public ICommand SaveCommand { get; }

    public ICommand DeleteCommand { get; }

    public ICommand PreviousDayCommand { get; }

    public ICommand NextDayCommand { get; }

    /// <summary>新規登録として開く。</summary>
    /// <param name="staff">選べるスタッフ。</param>
    /// <param name="workDate">既定の営業日。</param>
    /// <param name="businessDayStart">店舗の1日の開始時刻。翌日判定の基準。</param>
    public void OpenForAdd(
        IEnumerable<StaffOption> staff, DateOnly workDate, TimeOnly businessDayStart)
    {
        _businessDayStart = businessDayStart;
        _nextDayTouched = false;
        ReviewReasonText = null;

        SetStaffOptions(staff);

        Punches.Clear();
        Punches.Add(PunchOption.New);
        SelectedPunch = PunchOption.New;

        WorkDate = workDate;
        IsNextDay = false;
        Time = string.Empty;
        Note = string.Empty;
    }

    /// <summary>一覧の行から開く。その日の打刻を選んで直せるようにする。</summary>
    public void OpenForRow(
        IEnumerable<StaffOption> staff, AttendanceRow row, TimeOnly businessDayStart)
    {
        ArgumentNullException.ThrowIfNull(row);

        _businessDayStart = businessDayStart;
        _nextDayTouched = false;

        // なぜ要確認なのかは、直しに来たこの画面で伝える。
        // 一覧には最初の出勤と最後の退勤しか出ないため、
        // バッジだけでは、どの打刻を直せばよいのか分からない。
        ReviewReasonText = row.Summary.ReviewReasons.Count > 0
            ? string.Join(Environment.NewLine, row.Summary.ReviewReasons)
            : null;

        SetStaffOptions(staff);

        Punches.Clear();

        foreach (var record in row.Summary.Records)
        {
            Punches.Add(new PunchOption(record, Describe(record)));
        }

        // 「追加」も選べるようにする。打刻漏れの補填はこの行から行うのが自然。
        Punches.Add(PunchOption.New);

        WorkDate = row.WorkDate;
        SelectedStaff = StaffOptions.FirstOrDefault(o => o.Id == row.Summary.StaffId)
            ?? StaffOptions.FirstOrDefault();
        SelectedPunch = Punches[0];
    }

    public static string Describe(TimeRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        var method = record.EntryMethod switch
        {
            EntryMethod.ManualAdd => "手入力",
            EntryMethod.ManualEdit => "手修正",
            _ => "QR",
        };

        return $"{record.RecordedAt:HH:mm}　{PunchService.Describe(record.RecordType)}　[{method}]";
    }

    /// <summary>4桁を時刻に組み立てる。範囲外なら null。</summary>
    public DateTime? ParseTime()
    {
        if (Time.Length != TimeDigits || !Time.All(char.IsAsciiDigit))
        {
            return null;
        }

        var hour = int.Parse(Time[..2], Japanese);
        var minute = int.Parse(Time[2..], Japanese);

        if (hour > 23 || minute > 59)
        {
            return null;
        }

        // 深夜勤務では営業日と暦日がずれる。「翌日」の指定を暦日に反映する。
        return WorkDate.AddDays(IsNextDay ? 1 : 0).ToDateTime(new TimeOnly(hour, minute));
    }

    private async Task SaveAsync()
    {
        if (ParseTime() is not { } recordedAt || SelectedStaff?.Id is not { } staffId)
        {
            return;
        }

        var draft = new TimeRecordDraft
        {
            RecordId = SelectedPunch.Record?.Id,
            StaffId = staffId,
            WorkDate = WorkDate,
            RecordType = SelectedRecordType.Type,
            RecordedAt = recordedAt,
            Note = Note,
        };

        IsBusy = true;
        ErrorMessage = null;

        try
        {
            var validation = await _editor.ValidateAsync(draft);

            if (!validation.IsValid)
            {
                ErrorMessage = validation.Error;
                return;
            }

            if (validation.HasWarning
                && !_dialogs.Confirm("確認", validation.Warning!, "このまま保存する", "やめる"))
            {
                return;
            }

            if (draft.RecordId is null)
            {
                await _editor.AddAsync(draft);
            }
            else
            {
                await _editor.UpdateAsync(draft);
            }

            Completed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            // 保存できなかったことを黙って閉じない。勤怠の証跡が欠ける。
            ErrorMessage = $"保存できませんでした。{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task DeleteAsync()
    {
        if (SelectedPunch.Record is not { } record)
        {
            return;
        }

        // 削除は取り消せない。必ず確認を取る（→ plan.md 11-3）。
        if (!_dialogs.Confirm(
            "打刻を削除しますか？",
            $"{WorkDateText} の {Describe(record)} を削除します。この操作は取り消せません。",
            "削除する",
            "やめる"))
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;

        try
        {
            await _editor.DeleteAsync(record.Id);
            Completed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"削除できませんでした。{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void SetStaffOptions(IEnumerable<StaffOption> staff)
    {
        StaffOptions.Clear();

        // 「全スタッフ」は登録先として選べない。個人を必ず選ばせる。
        foreach (var option in staff.Where(o => o.Id is not null))
        {
            StaffOptions.Add(option);
        }

        SelectedStaff = StaffOptions.FirstOrDefault();
    }

    partial void OnSelectedPunchChanged(PunchOption value)
    {
        if (value.Record is { } record)
        {
            SelectedRecordType = RecordTypes.First(t => t.Type == record.RecordType);

            // 保存されている暦日から復元する。時刻だけ読むと翌日ぶんが当日に戻る。
            _nextDayTouched = true;
            IsNextDay = DateOnly.FromDateTime(record.RecordedAt) > record.WorkDate;

            Time = record.RecordedAt.ToString("HHmm", Japanese);
            Note = record.Note ?? string.Empty;
        }
        else
        {
            _nextDayTouched = false;
            IsNextDay = false;
            Time = string.Empty;
            Note = string.Empty;
        }

        OnPropertyChanged(nameof(CanChooseStaff));
        OnPropertyChanged(nameof(Title));
        NotifyCommandsChanged();
    }

    partial void OnWorkDateChanged(DateOnly value)
    {
        OnPropertyChanged(nameof(WorkDateText));
        OnPropertyChanged(nameof(RecordedAtText));
    }

    partial void OnIsNextDayChanged(bool value) => OnPropertyChanged(nameof(RecordedAtText));

    partial void OnTimeChanged(string value)
    {
        ApplyNextDayDefault();

        OnPropertyChanged(nameof(TimeText));
        OnPropertyChanged(nameof(RecordedAtText));
        NotifyCommandsChanged();
    }

    /// <summary>
    /// 時刻が揃った時点で、翌日かどうかの既定を決める。
    ///
    /// 開始 09:00 の店舗で 02:00 と入れたなら、それは翌日の未明。
    /// QR で打刻したときと同じ暦日になるよう合わせる。
    /// 手で切り替えたあとは上書きしない。
    /// </summary>
    private void ApplyNextDayDefault()
    {
        if (_nextDayTouched || Time.Length != TimeDigits || !Time.All(char.IsAsciiDigit))
        {
            return;
        }

        var hour = int.Parse(Time[..2], Japanese);
        var minute = int.Parse(Time[2..], Japanese);

        if (hour > 23 || minute > 59)
        {
            return;
        }

        IsNextDay = new TimeOnly(hour, minute) < _businessDayStart;
    }

    /// <summary>「翌日」を手で切り替える。以後は自動判定で上書きしない。</summary>
    public void ToggleNextDay(bool value)
    {
        _nextDayTouched = true;
        IsNextDay = value;
    }

    partial void OnSelectedStaffChanged(StaffOption? value) => NotifyCommandsChanged();

    partial void OnIsBusyChanged(bool value) => NotifyCommandsChanged();

    private void NotifyCommandsChanged()
    {
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(CanDelete));

        (SaveCommand as IRelayCommand)?.NotifyCanExecuteChanged();
        (DeleteCommand as IRelayCommand)?.NotifyCanExecuteChanged();
    }
}
