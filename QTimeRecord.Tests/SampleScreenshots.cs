using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using QTimeRecord.App.Controls;
using QTimeRecord.App.Services;
using QTimeRecord.Core.Domain;
using QTimeRecord.Core.Services;
using QTimeRecord.App.ViewModels;
using QTimeRecord.App.Views;
using QTimeRecord.App.Views.Dialogs;

namespace QTimeRecord.Tests;

/// <summary>
/// 画面の見た目を PNG に書き出す。通常のテスト実行では動かさない。
/// 実行するには Skip を外して:
///   dotnet test --filter "FullyQualifiedName~SampleScreenshots"
/// </summary>
public sealed class SampleScreenshots
{
    private static string Output(string name)
        => Path.Combine(Path.GetTempPath(), $"qtimerecord-{name}.png");

    /// <summary>
    /// XAML は名前で束縛するため、表示に必要なプロパティを持つ器があれば描ける。
    /// 実物の ViewModel は依存が多く、見た目の確認には過剰。
    /// </summary>
    private sealed record IdleStub(
        string CompanyName,
        string StoreName,
        bool IsScannerReady,
        string ScannerStatusText,
        DateTime Now,
        string? ErrorMessage,
        string? AnnouncementTitle,
        ObservableCollection<AnnouncementItem> Announcements)
    {
        public bool HasAnnouncements => Announcements.Count > 0;

        public string? ScannerNotice => IsScannerReady
            ? null
            : "QRリーダーとの接続が切れました。再接続しています。";

        public ICommand AdminLoginCommand => new RelayCommand(() => { });
    }

    /// <summary>入力欄は双方向束縛のため、読み取り専用の器では描画できない。</summary>
    private sealed class SetupStub
    {
        public string CompanyName { get; set; } = string.Empty;

        public string StoreName { get; set; } = string.Empty;

        public string StoreCode { get; set; } = string.Empty;

        public string BusinessDayStart { get; set; } = string.Empty;

        public string BusinessDayEnd { get; set; } = string.Empty;

        public string AdminPin { get; set; } = string.Empty;

        public string? ErrorMessage { get; set; }
    }

    [Fact(Skip = "目視確認用。必要なときだけ Skip を外して実行する。")]
    public void 待機画面()
    {
        var stub = new IdleStub(
            "株式会社クオリティフーズ",
            "相模原店",
            IsScannerReady: true,
            "リーダー待機中",
            new DateTime(2026, 9, 6, 13, 53, 17),
            ErrorMessage: null,
            "【店舗連絡】今週の重要共有事項",
            [
                new AnnouncementItem("健康診断", "受診希望日を月末までに提出願います。"),
                new AnnouncementItem("衛生管理", "検温と手指アルコール消毒を励行してください。"),
                new AnnouncementItem("シフト", "来月前半の希望シフトは今週日曜20:00締切です。"),
            ]);

        ViewRenderer.SaveAsPng<IdleView>(stub, Output("idle"), 1366, 768);
    }

    [Fact(Skip = "目視確認用。必要なときだけ Skip を外して実行する。")]
    public void 待機画面_リーダー未接続とエラー表示()
    {
        var stub = new IdleStub(
            "株式会社クオリティフーズ",
            "相模原店",
            IsScannerReady: false,
            "リーダー未接続",
            new DateTime(2026, 9, 6, 13, 53, 17),
            "登録されていないQRコードです。管理者へ連絡してください。",
            AnnouncementTitle: null,
            []);

        ViewRenderer.SaveAsPng<IdleView>(stub, Output("idle-error"), 1366, 768);
    }

    [Fact(Skip = "目視確認用。必要なときだけ Skip を外して実行する。")]
    public void 初回セットアップ画面()
    {
        var stub = new SetupStub
        {
            CompanyName = "株式会社クオリティフーズ",
            StoreName = "相模原店",
            StoreCode = "101",
            BusinessDayStart = "11:00",
            BusinessDayEnd = "05:00",
            AdminPin = "12345678",
        };

        ViewRenderer.SaveAsPng<SetupView>(stub, Output("setup"), 1366, 768);
    }

    private sealed record PunchSelectStub(
        string StaffName,
        string? StaffNo,
        string? EmploymentType,
        string? LastPunchText,
        int RemainingSeconds,
        string? ScannerWarning,
        bool CanPunch)
    {
        public int TimeoutSeconds => 30;

        /// <summary>
        /// ボタンの有効・無効は CanExecute で決まる。
        /// 器にコマンドが無いと、押せないはずの状態も押せる見た目で描かれてしまう。
        /// </summary>
        public ICommand PunchCommand => new RelayCommand(() => { }, () => CanPunch);

        public ICommand CancelCommand => new RelayCommand(() => { });
    }

    private sealed record PunchResultStub(
        string StaffName,
        string Headline,
        string RecordTypeLabel,
        DateTime RecordedAt,
        bool IsOutsideBusinessHours)
    {
        public string OutsideBusinessHoursNote =>
            "営業時間外の打刻として記録しました。管理者の確認対象になります。";
    }

    [Fact(Skip = "目視確認用。必要なときだけ Skip を外して実行する。")]
    public void 打刻種別の選択画面()
    {
        var stub = new PunchSelectStub(
            "田中 健一",
            "E-0104",
            "アルバイト",
            "9/5 21:30 退勤",
            RemainingSeconds: 22,
            ScannerWarning: null,
            CanPunch: true);

        ViewRenderer.SaveAsPng<PunchSelectView>(stub, Output("punch-select"), 1366, 768);
    }

    [Fact(Skip = "目視確認用。必要なときだけ Skip を外して実行する。")]
    public void 打刻種別の選択画面_リーダー未接続()
    {
        var stub = new PunchSelectStub(
            "田中 健一",
            StaffNo: null,
            EmploymentType: null,
            LastPunchText: null,
            RemainingSeconds: 8,
            "QRリーダーが接続されていません。管理者へ連絡してください。",
            CanPunch: false);

        ViewRenderer.SaveAsPng<PunchSelectView>(stub, Output("punch-select-offline"), 1366, 768);
    }

    [Fact(Skip = "目視確認用。必要なときだけ Skip を外して実行する。")]
    public void 打刻完了画面()
    {
        var stub = new PunchResultStub(
            "田中 健一",
            "出勤を記録しました",
            "出勤",
            new DateTime(2026, 9, 6, 8, 58, 43),
            IsOutsideBusinessHours: false);

        ViewRenderer.SaveAsPng<PunchResultView>(stub, Output("punch-result"), 1366, 768);
    }

    [Fact(Skip = "目視確認用。必要なときだけ Skip を外して実行する。")]
    public void 打刻完了画面_営業時間外()
    {
        var stub = new PunchResultStub(
            "田中 健一",
            "退勤を記録しました",
            "退勤",
            new DateTime(2026, 9, 6, 6, 12, 5),
            IsOutsideBusinessHours: true);

        ViewRenderer.SaveAsPng<PunchResultView>(stub, Output("punch-result-outside"), 1366, 768);
    }

    private sealed class AdminLoginStub
    {
        public string StoreName { get; set; } = string.Empty;

        public string CompanyName { get; set; } = string.Empty;

        /// <summary>テンキーが書き込むため、読み取り専用の器では描画できない。</summary>
        public string Pin { get; set; } = string.Empty;

        public int MaxLength { get; set; } = 8;

        public string MaskedPin => PinEntry.Mask(Pin);

        public string PinIndicator => PinEntry.Indicator(Pin, MaxLength);

        public string? ErrorMessage { get; set; }

        public string? LockMessage { get; set; }

        public bool IsLocked { get; set; }

        public ICommand SubmitCommand => new RelayCommand(() => { }, () => !IsLocked);

        public ICommand CancelCommand => new RelayCommand(() => { });
    }

    private sealed record AdminShellStub(
        ObservableCollection<AdminTab> Tabs, AdminTab SelectedTab, object Content)
    {
        public ICommand LogoutCommand => new RelayCommand(() => { });
    }

    [Fact(Skip = "目視確認用。必要なときだけ Skip を外して実行する。")]
    public void 管理者ログイン画面()
    {
        var stub = new AdminLoginStub
        {
            StoreName = "相模原店",
            CompanyName = "株式会社クオリティフーズ",
            Pin = "1234",
        };

        ViewRenderer.SaveAsPng<AdminLoginView>(stub, Output("admin-login"), 1366, 768);
    }

    [Fact(Skip = "目視確認用。必要なときだけ Skip を外して実行する。")]
    public void 管理者ログイン画面_ロック中()
    {
        var stub = new AdminLoginStub
        {
            StoreName = "相模原店",
            CompanyName = "株式会社クオリティフーズ",
            IsLocked = true,
            LockMessage = "連続して間違えたため、あと 4分12秒 ロックされています。",
        };

        ViewRenderer.SaveAsPng<AdminLoginView>(stub, Output("admin-login-locked"), 1366, 768);
    }

    [Fact(Skip = "目視確認用。必要なときだけ Skip を外して実行する。")]
    public void 管理画面シェル()
    {
        var tabs = new ObservableCollection<AdminTab>
        {
            new("勤務状況", () => new AdminPlaceholder("勤務状況", "この画面は plan.md 11章 で作ります。")),
            new("スタッフ管理", () => new AdminPlaceholder("スタッフ管理", "この画面は plan.md 12章 で作ります。")),
            new("店舗設定", () => new AdminPlaceholder("店舗設定", "この画面は plan.md 13章 で作ります。")),
        };

        var stub = new AdminShellStub(tabs, tabs[0], tabs[0].CreateContent());

        ViewRenderer.SaveAsPng<AdminShellView>(stub, Output("admin-shell"), 1366, 768);
    }

    private sealed class AttendanceStub
    {
        public string MonthLabel { get; set; } = string.Empty;

        public string CountLabel { get; set; } = string.Empty;

        public ObservableCollection<AttendanceRowView> Rows { get; } = [];

        public ObservableCollection<StaffOption> StaffOptions { get; } = [];

        public StaffOption? SelectedStaff { get; set; }

        public FilterOption SelectedFilter { get; set; } = AttendanceViewModel.Filters[0];

        public bool IsBusy { get; set; }

        public bool IsEmpty { get; set; }

        public string? ErrorMessage { get; set; }

        public bool IsExporting { get; set; }

        public string? ExportedPath { get; set; }

        public string SortLabel { get; set; } = "新しい日付順 ▼";

        public ICommand ToggleSortCommand => new RelayCommand(() => { });

        public ICommand ExportCsvCommand => new RelayCommand(() => { });

        public ICommand OpenExportFolderCommand => new RelayCommand(() => { });

        public ICommand PreviousMonthCommand => new RelayCommand(() => { });

        public ICommand NextMonthCommand => new RelayCommand(() => { });

        public ICommand ThisMonthCommand => new RelayCommand(() => { });

        public ICommand AddCommand => new RelayCommand(() => { });

        public ICommand EditCommand => new RelayCommand(() => { });
    }

    [Fact(Skip = "目視確認用。必要なときだけ Skip を外して実行する。")]
    public void 勤務状況一覧()
    {
        var stub = new AttendanceStub
        {
            MonthLabel = "2026年9月",
            CountLabel = "全 5 件",
            ExportedPath = @"C:\Users\tencho\Documents\QTimeRecord_勤務状況_101_202609.csv",
        };

        stub.StaffOptions.Add(StaffOption.Everyone);
        stub.SelectedStaff = stub.StaffOptions[0];

        // 既定の並び（新しい日付が上、同じ日は遅い出勤が上）。
        stub.Rows.Add(SampleRow("伊藤 翔太", "アルバイト", new DateOnly(2026, 9, 6),
            "17:00", null, []));
        stub.Rows.Add(SampleRow("山田 太郎", "正社員", new DateOnly(2026, 9, 6),
            "09:00", "18:30", [("12:00", "12:30"), ("15:00", "15:30")]));
        stub.Rows.Add(SampleRow("高橋 美咲", "パート", new DateOnly(2026, 9, 5),
            "10:00", "15:30", [("12:00", "12:45")], EntryMethod.ManualEdit, "打刻機の不具合のため代理打刻"));
        stub.Rows.Add(SampleRow("鈴木 一平", "正社員", new DateOnly(2026, 9, 5),
            "08:50", "19:10", [("12:00", "13:30")], EntryMethod.ManualAdd, "外出先直行のため手動登録"));
        stub.Rows.Add(SampleRow("田中 健一", "アルバイト", new DateOnly(2026, 9, 4),
            "09:02", "18:05", [("12:00", "13:00")]));

        ViewRenderer.SaveAsPng<AttendanceView>(stub, Output("attendance"), 1318, 568);
    }

    private static AttendanceRowView SampleRow(
        string name,
        string employmentType,
        DateOnly workDate,
        string clockIn,
        string? clockOut,
        (string Start, string End)[] breaks,
        EntryMethod method = EntryMethod.Qr,
        string? note = null)
    {
        var staffId = Guid.CreateVersion7();
        var records = new List<TimeRecord>();

        void Add(TimeRecordType type, string time) => records.Add(new TimeRecord
        {
            Id = Guid.CreateVersion7(),
            StoreId = Guid.CreateVersion7(),
            StaffId = staffId,
            RecordType = type,
            RecordedAt = workDate.ToDateTime(
                TimeOnly.Parse(time, System.Globalization.CultureInfo.InvariantCulture)),
            WorkDate = workDate,
            EntryMethod = method,
            Note = note,
        });

        Add(TimeRecordType.ClockIn, clockIn);

        foreach (var (start, end) in breaks)
        {
            Add(TimeRecordType.BreakStart, start);
            Add(TimeRecordType.BreakEnd, end);
        }

        if (clockOut is not null)
        {
            Add(TimeRecordType.ClockOut, clockOut);
        }

        var summary = AttendanceAggregator.Summarize(staffId, workDate, records);

        return new AttendanceRowView(new AttendanceRow(summary, name, "E-01", employmentType));
    }

    [Fact(Skip = "目視確認用。必要なときだけ Skip を外して実行する。")]
    public void 打刻の修正ダイアログ()
    {
        var editor = new NoopEditService();
        var viewModel = new TimeRecordEditViewModel(editor, new NoopDialogService());

        var workDate = new DateOnly(2026, 9, 5);
        var staffId = Guid.CreateVersion7();

        var records = new List<TimeRecord>
        {
            SampleRecord(staffId, workDate, TimeRecordType.ClockIn, "10:00", EntryMethod.Qr),
            SampleRecord(staffId, workDate, TimeRecordType.BreakStart, "12:00", EntryMethod.Qr),
            SampleRecord(staffId, workDate, TimeRecordType.BreakEnd, "12:45", EntryMethod.Qr),
            SampleRecord(
                staffId, workDate, TimeRecordType.ClockOut, "15:30", EntryMethod.ManualEdit,
                "打刻機の不具合のため代理打刻"),
        };

        var summary = AttendanceAggregator.Summarize(staffId, workDate, records);

        viewModel.OpenForRow(
            [new StaffOption(staffId, "高橋 美咲"), new StaffOption(Guid.CreateVersion7(), "田中 健一")],
            new AttendanceRow(summary, "高橋 美咲", "E-0182", "パート"),
            new TimeOnly(9, 0));

        viewModel.SelectedPunch = viewModel.Punches[3];

        ViewRenderer.SaveAsPng<TimeRecordEditPanel>(viewModel, Output("time-record-edit"), 980, 768);
    }

    /// <summary>
    /// 深夜勤務の退勤を手入力した状態。営業日 9/5 のまま暦日が 9/6 になることを
    /// 保存前に見せられているかを、ここで確かめる。
    /// </summary>
    [Fact(Skip = "目視確認用。必要なときだけ Skip を外して実行する。")]
    public void 打刻の修正ダイアログ_翌日()
    {
        var editor = new NoopEditService();
        var viewModel = new TimeRecordEditViewModel(editor, new NoopDialogService());

        var workDate = new DateOnly(2026, 9, 5);
        var staffId = Guid.CreateVersion7();

        var records = new List<TimeRecord>
        {
            SampleRecord(staffId, workDate, TimeRecordType.ClockIn, "21:30", EntryMethod.Qr),
        };

        var summary = AttendanceAggregator.Summarize(staffId, workDate, records);

        viewModel.OpenForRow(
            [new StaffOption(staffId, "高橋 美咲"), new StaffOption(Guid.CreateVersion7(), "田中 健一")],
            new AttendanceRow(summary, "高橋 美咲", "E-0182", "パート"),
            new TimeOnly(11, 0));

        viewModel.SelectedPunch = viewModel.Punches[^1];
        viewModel.SelectedRecordType = TimeRecordEditViewModel.RecordTypes.First(
            t => t.Type == TimeRecordType.ClockOut);
        viewModel.Time = "0215";

        ViewRenderer.SaveAsPng<TimeRecordEditPanel>(
            viewModel, Output("time-record-edit-nextday"), 980, 768);
    }

    private static TimeRecord SampleRecord(
        Guid staffId,
        DateOnly workDate,
        TimeRecordType type,
        string time,
        EntryMethod method,
        string? note = null) => new()
    {
        Id = Guid.CreateVersion7(),
        StoreId = Guid.CreateVersion7(),
        StaffId = staffId,
        RecordType = type,
        RecordedAt = workDate.ToDateTime(
            TimeOnly.Parse(time, System.Globalization.CultureInfo.InvariantCulture)),
        WorkDate = workDate,
        EntryMethod = method,
        Note = note,
    };

    /// <summary>描画だけを見るための空実装。保存も検証も行わない。</summary>
    private sealed class NoopEditService : ITimeRecordEditService
    {
        public Task<EditValidation> ValidateAsync(TimeRecordDraft draft, CancellationToken ct = default)
            => Task.FromResult(EditValidation.Ok);

        public Task<TimeRecord> AddAsync(TimeRecordDraft draft, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<TimeRecord> UpdateAsync(TimeRecordDraft draft, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task DeleteAsync(Guid recordId, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class NoopDialogService : IDialogService
    {
        public bool Confirm(
            string title, string message, string okText = "OK", string cancelText = "キャンセル")
            => false;

        public void ShowInfo(string title, string message)
        {
        }

        public void ShowError(string title, string message)
        {
        }
    }

    private sealed class StaffStub
    {
        public ObservableCollection<StaffListRow> Rows { get; } = [];

        public StaffListRow? SelectedStaff { get; set; }

        public StaffFilterOption SelectedFilter { get; set; } = StaffViewModel.Filters[0];

        public StaffStatusOption? SelectedStatus { get; set; }

        public string Search { get; set; } = string.Empty;

        public byte[]? CardPng { get; set; }

        public string CountLabel { get; set; } = string.Empty;

        public bool HasSelection => SelectedStaff is not null;

        public bool IsEmpty => Rows.Count == 0;

        public bool IsBusy => false;

        public string? ErrorMessage => null;

        public ICommand AddCommand => new RelayCommand(() => { });

        public ICommand EditCommand => new RelayCommand(() => { });

        public ICommand ReissueQrCommand => new RelayCommand(() => { });

        public ICommand SaveCardCommand => new RelayCommand(() => { });
    }

    [Fact(Skip = "目視確認用。必要なときだけ Skip を外して実行する。")]
    public void スタッフ管理画面()
    {
        var stub = new StaffStub { CountLabel = "全 5 名" };

        stub.Rows.Add(StaffRow("田中 健一", "タナカ ケンイチ", "E-0104", "アルバイト",
            StaffStatus.Active, new DateTime(2026, 4, 1, 9, 0, 0)));
        stub.Rows.Add(StaffRow("佐藤 義明", "サトウ ヨシアキ", "M-0001", "社員",
            StaffStatus.Active, new DateTime(2025, 11, 15, 9, 0, 0)));
        stub.Rows.Add(StaffRow("高橋 美咲", "タカハシ ミサキ", "P-0210", "パート",
            StaffStatus.Active, new DateTime(2026, 1, 10, 9, 0, 0)));
        stub.Rows.Add(StaffRow("渡辺 翔太", "ワタナベ ショウタ", "E-0142", "アルバイト",
            StaffStatus.OnLeave, new DateTime(2025, 9, 20, 9, 0, 0)));
        stub.Rows.Add(StaffRow("伊藤 健治", "イトウ ケンジ", "E-0089", "アルバイト",
            StaffStatus.Retired, issuedAt: null));

        stub.SelectedStaff = stub.Rows[0];
        stub.SelectedStatus = StaffViewModel.StatusOptions[0];
        stub.CardPng = SampleCardPng();

        ViewRenderer.SaveAsPng<StaffView>(stub, Output("staff"), 1318, 568);
    }

    [Fact(Skip = "目視確認用。必要なときだけ Skip を外して実行する。")]
    public void スタッフ登録ダイアログ()
    {
        var viewModel = new StaffEditViewModel(new NoopStaffService());

        viewModel.OpenForAdd();
        viewModel.Name = "田中 健一";
        viewModel.NameKana = "タナカ ケンイチ";
        viewModel.StaffNo = "E-0104";
        viewModel.EmploymentType = "アルバイト";

        ViewRenderer.SaveAsPng<StaffEditPanel>(viewModel, Output("staff-edit"), 720, 700);
    }

    private static StaffListRow StaffRow(
        string name,
        string kana,
        string staffNo,
        string employmentType,
        StaffStatus status,
        DateTime? issuedAt) => new(new StaffListItem(
            new Staff
            {
                Id = Guid.CreateVersion7(),
                StoreId = Guid.CreateVersion7(),
                Name = name,
                NameKana = kana,
                StaffNo = staffNo,
                EmploymentType = employmentType,
                Status = status,
            },
            issuedAt));

    /// <summary>本物のカード描画を通す。見た目の確認なので実物でなければ意味がない。</summary>
    private static byte[] SampleCardPng()
    {
        var qr = new QrCodeImageGenerator().CreatePng("SAMPLETOKEN0123456789ABCDEFGHIJK");

        return new QrCardRenderer().RenderPng(new QrCardContent
        {
            StoreName = "相模原店",
            StaffName = "田中 健一",
            StaffNo = "E-0104",
            NameKana = "タナカ ケンイチ",
            IssuedOn = new DateOnly(2026, 4, 1),
            QrPng = qr,
        });
    }

    private sealed class NoopStaffService : IStaffService
    {
        public Task<IReadOnlyList<StaffListItem>> ListAsync(
            StaffFilter filter = StaffFilter.All,
            string? search = null,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<StaffListItem>>([]);

        public Task<string?> ValidateAsync(StaffDraft draft, CancellationToken ct = default)
            => Task.FromResult<string?>(null);

        public Task<Staff> AddAsync(StaffDraft draft, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<Staff> UpdateAsync(StaffDraft draft, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<Staff> ChangeStatusAsync(
            Guid staffId, StaffStatus status, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<StaffQrToken> ReissueQrAsync(Guid staffId, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class StoreSettingsStub
    {
        public string CompanyName { get; set; } = string.Empty;

        public string StoreName { get; set; } = string.Empty;

        public string StoreCode { get; set; } = string.Empty;

        public string BusinessDayStart { get; set; } = string.Empty;

        public string BusinessDayEnd { get; set; } = string.Empty;

        public string AnnouncementTitle { get; set; } = string.Empty;

        public ObservableCollection<AnnouncementEditItem> Announcements { get; } = [];

        public ObservableCollection<string> AvailablePorts { get; } = [];

        public string? ComPort { get; set; }

        public int BaudRate { get; set; } = 9600;

        public int DataBits { get; set; } = 8;

        public string Parity { get; set; } = "None";

        public string StopBits { get; set; } = "One";

        public string AnnouncementCountText => $"{Announcements.Count} / 5 件";

        public bool CanAddAnnouncement => Announcements.Count < 5;

        public string ScannerStatusText { get; set; } = "接続中";

        public bool IsDirty { get; set; }

        public string? ErrorMessage { get; set; }

        public string? TestResult { get; set; }

        public bool TestSucceeded { get; set; }

        public ICommand SaveCommand => new RelayCommand(() => { }, () => IsDirty);

        public ICommand DiscardCommand => new RelayCommand(() => { }, () => IsDirty);

        public ICommand AddAnnouncementCommand => new RelayCommand(() => { }, () => CanAddAnnouncement);

        public ICommand RemoveAnnouncementCommand => new RelayCommand(() => { });

        public ICommand RefreshPortsCommand => new RelayCommand(() => { });

        public ICommand ConnectCommand => new RelayCommand(() => { });

        public ICommand DisconnectCommand => new RelayCommand(() => { });

        public ICommand TestConnectionCommand => new RelayCommand(() => { });
    }

    [Fact(Skip = "目視確認用。必要なときだけ Skip を外して実行する。")]
    public void 店舗設定画面()
    {
        var stub = new StoreSettingsStub
        {
            CompanyName = "株式会社クオリティフーズ",
            StoreName = "相模原店",
            StoreCode = "101",
            BusinessDayStart = "11:00",
            BusinessDayEnd = "05:00",
            AnnouncementTitle = "【店舗連絡】今週の重要共有事項（検温・衛生・希望シフト提出）",
            ComPort = "COM3",
            IsDirty = true,
            TestResult = "COM3 で読み取りを確認しました。",
            TestSucceeded = true,
        };

        stub.AvailablePorts.Add("COM1");
        stub.AvailablePorts.Add("COM3");

        stub.Announcements.Add(new AnnouncementEditItem
        {
            Heading = "健康診断",
            Body = "受診希望日を月末までに提出願います。",
        });
        stub.Announcements.Add(new AnnouncementEditItem
        {
            Heading = "衛生管理",
            Body = "検温と手指アルコール消毒を励行してください。",
        });
        stub.Announcements.Add(new AnnouncementEditItem
        {
            Heading = "シフト",
            Body = "来月前半の希望シフトは今週日曜20:00締切です。",
        });

        ViewRenderer.SaveAsPng<StoreSettingsView>(stub, Output("store-settings"), 1318, 568);
    }

    /// <summary>
    /// 1920x1080 での確認（→ plan.md 16-3-8）。
    /// 基準の 1366x768 で作っているため、広い画面で間延びしないかを見る。
    /// </summary>
    [Fact(Skip = "目視確認用。必要なときだけ Skip を外して実行する。")]
    public void 高解像度での確認()
    {
        const int width = 1920;
        const int height = 1080;

        var idle = new IdleStub(
            "株式会社クオリティフーズ",
            "相模原店",
            IsScannerReady: true,
            "リーダー待機中",
            new DateTime(2026, 9, 6, 13, 53, 17),
            ErrorMessage: null,
            "【店舗連絡】今週の重要共有事項",
            [
                new AnnouncementItem("健康診断", "受診希望日を月末までに提出願います。"),
                new AnnouncementItem("衛生管理", "検温と手指アルコール消毒を励行してください。"),
                new AnnouncementItem("シフト", "来月前半の希望シフトは今週日曜20:00締切です。"),
            ]);

        ViewRenderer.SaveAsPng<IdleView>(idle, Output("hd-idle"), width, height);

        var select = new PunchSelectStub(
            "田中 健一", "E-0104", "アルバイト", "9/5 21:30 退勤",
            RemainingSeconds: 22, ScannerWarning: null, CanPunch: true);

        ViewRenderer.SaveAsPng<PunchSelectView>(select, Output("hd-punch-select"), width, height);

        var attendance = new AttendanceStub
        {
            MonthLabel = "2026年9月",
            CountLabel = "全 3 件",
        };

        attendance.StaffOptions.Add(StaffOption.Everyone);
        attendance.SelectedStaff = attendance.StaffOptions[0];

        attendance.Rows.Add(SampleRow("田中 健一", "アルバイト", new DateOnly(2026, 9, 4),
            "09:02", "18:05", [("12:00", "13:00")]));
        attendance.Rows.Add(SampleRow("高橋 美咲", "パート", new DateOnly(2026, 9, 5),
            "10:00", "15:30", [("12:00", "12:45")], EntryMethod.ManualEdit, "打刻機の不具合のため代理打刻"));
        attendance.Rows.Add(SampleRow("伊藤 翔太", "アルバイト", new DateOnly(2026, 9, 6),
            "17:00", null, []));

        ViewRenderer.SaveAsPng<AttendanceView>(
            attendance, Output("hd-attendance"), width - 48, height - 200);
    }
}
