using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using QTimeRecord.App.Services;
using QTimeRecord.Core.Devices;
using QTimeRecord.Core.Services;

namespace QTimeRecord.App.ViewModels;

/// <summary>編集中のお知らせ1件。</summary>
public sealed partial class AnnouncementEditItem : ObservableObject
{
    [ObservableProperty]
    private string _heading = string.Empty;

    [ObservableProperty]
    private string _body = string.Empty;
}

/// <summary>
/// 店舗設定。店舗情報・営業日・お知らせ・リーダー設定をまとめて編集する。
///
/// <b>保存は1回でまとめて行う。</b>項目ごとに保存すると、
/// 途中まで反映された中途半端な設定が残る。
/// </summary>
public sealed partial class StoreSettingsViewModel : ObservableObject, IConfirmNavigation
{
    /// <summary>接続テストで読み取りを待つ時間（→ plan.md 13-3）。</summary>
    public static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    private readonly IStoreSettingsService _settings;
    private readonly IQrScannerService _scanner;
    private readonly ISerialPortEnumerator _ports;
    private readonly ScannerHost _scannerHost;
    private readonly IDialogService _dialogs;
    private readonly ILogger<StoreSettingsViewModel> _logger;

    private Guid _storeId;
    private bool _loading;

    public StoreSettingsViewModel(
        IStoreSettingsService settings,
        IQrScannerService scanner,
        ISerialPortEnumerator ports,
        ScannerHost scannerHost,
        IDialogService dialogs,
        ILogger<StoreSettingsViewModel> logger)
    {
        _settings = settings;
        _scanner = scanner;
        _ports = ports;
        _scannerHost = scannerHost;
        _dialogs = dialogs;
        _logger = logger;

        SaveCommand = new AsyncRelayCommand(SaveAsync, () => IsDirty && !IsBusy);
        DiscardCommand = new AsyncRelayCommand(DiscardAsync, () => IsDirty && !IsBusy);
        AddAnnouncementCommand = new RelayCommand(AddAnnouncement, () => CanAddAnnouncement);
        RemoveAnnouncementCommand = new RelayCommand<AnnouncementEditItem?>(RemoveAnnouncement);
        RefreshPortsCommand = new RelayCommand(RefreshPorts);
        ConnectCommand = new AsyncRelayCommand(ConnectAsync, () => !IsBusy);
        DisconnectCommand = new RelayCommand(Disconnect);
        TestConnectionCommand = new AsyncRelayCommand(TestConnectionAsync, () => !IsBusy);

        // 入力のどれかが変われば未保存になる。項目ごとに書くと足し忘れる。
        PropertyChanged += OnAnyPropertyChanged;
        Announcements.CollectionChanged += OnAnnouncementsChanged;
    }

    public static IReadOnlyList<int> BaudRates { get; } = [4800, 9600, 19200, 38400, 57600, 115200];

    public static IReadOnlyList<int> DataBitsOptions { get; } = [7, 8];

    public static IReadOnlyList<string> ParityOptions { get; } = ["None", "Even", "Odd"];

    public static IReadOnlyList<string> StopBitsOptions { get; } = ["One", "Two"];

    public ObservableCollection<AnnouncementEditItem> Announcements { get; } = [];

    public ObservableCollection<string> AvailablePorts { get; } = [];

    [ObservableProperty]
    private string _companyName = string.Empty;

    [ObservableProperty]
    private string _storeName = string.Empty;

    [ObservableProperty]
    private string _storeCode = string.Empty;

    /// <summary>1日の開始時刻。営業日の境界になる。</summary>
    [ObservableProperty]
    private string _businessDayStart = "09:00";

    /// <summary>1日の終了時刻。営業時間外の判定に使う。</summary>
    [ObservableProperty]
    private string _businessDayEnd = "22:00";

    [ObservableProperty]
    private string _announcementTitle = string.Empty;

    [ObservableProperty]
    private string? _comPort;

    [ObservableProperty]
    private int _baudRate = 9600;

    [ObservableProperty]
    private int _dataBits = 8;

    [ObservableProperty]
    private string _parity = "None";

    [ObservableProperty]
    private string _stopBits = "One";

    [ObservableProperty]
    private bool _isDirty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>接続テストの結果。まだ試していなければ null。</summary>
    [ObservableProperty]
    private string? _testResult;

    [ObservableProperty]
    private bool _testSucceeded;

    [ObservableProperty]
    private ScannerState _scannerState;

    /// <summary>店舗ID。表示のみ。問い合わせのときに使う。</summary>
    public string StoreIdText => _storeId == Guid.Empty ? string.Empty : _storeId.ToString();

    public string AnnouncementCountText
        => $"{Announcements.Count} / {StoreSettingsService.MaxAnnouncements} 件";

    public bool CanAddAnnouncement => Announcements.Count < StoreSettingsService.MaxAnnouncements;

    public string ScannerStatusText => ScannerState switch
    {
        ScannerState.Connected => "接続中",
        ScannerState.Disconnected => "未接続",
        _ => "停止中",
    };

    public IRelayCommand SaveCommand { get; }

    public IRelayCommand DiscardCommand { get; }

    public IRelayCommand AddAnnouncementCommand { get; }

    public ICommand RemoveAnnouncementCommand { get; }

    public ICommand RefreshPortsCommand { get; }

    public IRelayCommand ConnectCommand { get; }

    public ICommand DisconnectCommand { get; }

    public IRelayCommand TestConnectionCommand { get; }

    /// <summary>現在の設定を読み込む。</summary>
    public async Task LoadAsync(CancellationToken ct = default)
    {
        IsBusy = true;

        try
        {
            var snapshot = await _settings.GetAsync(ct);

            if (snapshot is null)
            {
                ErrorMessage = "店舗が登録されていません。初期設定をやり直してください。";
                return;
            }

            _loading = true;

            _storeId = snapshot.Store.Id;
            CompanyName = snapshot.Store.CompanyName;
            StoreName = snapshot.Store.StoreName;
            StoreCode = snapshot.Store.StoreCode ?? string.Empty;
            BusinessDayStart = Format(snapshot.Store.BusinessDayStart);
            BusinessDayEnd = Format(snapshot.Store.BusinessDayEnd);
            AnnouncementTitle = snapshot.Store.AnnouncementTitle ?? string.Empty;

            Announcements.Clear();

            foreach (var announcement in snapshot.Announcements)
            {
                Announcements.Add(Track(new AnnouncementEditItem
                {
                    Heading = announcement.Heading,
                    Body = announcement.Body,
                }));
            }

            ComPort = snapshot.Device.ComPort;
            BaudRate = snapshot.Device.BaudRate;
            DataBits = snapshot.Device.DataBits;
            Parity = snapshot.Device.Parity;
            StopBits = snapshot.Device.StopBits;

            ScannerState = _scanner.State;

            RefreshPorts();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "店舗設定を読み込めませんでした。");
            ErrorMessage = "店舗設定を読み込めませんでした。";
        }
        finally
        {
            _loading = false;
            IsBusy = false;

            OnPropertyChanged(nameof(StoreIdText));
            NotifyAnnouncementCountChanged();

            // 通知をすべて出し切ってから落とす。先に落とすと、
            // 上の通知自体を「変更」とみなして未保存に戻ってしまう。
            IsDirty = false;
        }
    }

    /// <summary>
    /// 未保存のまま離れてよいか。
    ///
    /// 保存し忘れは「設定したのに反映されない」という形で現れ、
    /// 原因を突き止めるのが難しい。離れる前に必ず気づかせる。
    /// </summary>
    public bool CanLeave()
        => !IsDirty || _dialogs.Confirm(
            "変更を破棄しますか？",
            $"保存していない変更があります。{Environment.NewLine}このまま移動すると入力した内容は失われます。",
            "破棄して移動",
            "編集に戻る");

    /// <summary>入力を検証して保存用の形にする。書式が不正なら null。</summary>
    public StoreSettingsDraft? BuildDraft(out string? error)
    {
        if (!TryParseTime(BusinessDayStart, out var start))
        {
            error = "1日の開始時刻を HH:mm で入力してください。";
            return null;
        }

        if (!TryParseTime(BusinessDayEnd, out var end))
        {
            error = "1日の終了時刻を HH:mm で入力してください。";
            return null;
        }

        var draft = new StoreSettingsDraft
        {
            StoreName = StoreName,
            StoreCode = StoreCode,
            BusinessDayStart = start,
            BusinessDayEnd = end,
            AnnouncementTitle = AnnouncementTitle,
            Announcements = [.. Announcements.Select(a => new AnnouncementDraft(a.Heading, a.Body))],
            ComPort = ComPort,
            BaudRate = BaudRate,
            DataBits = DataBits,
            Parity = Parity,
            StopBits = StopBits,
        };

        error = _settings.Validate(draft);

        return error is null ? draft : null;
    }

    /// <summary>保存する。テストから直接呼べるよう公開する。</summary>
    public async Task SaveAsync()
    {
        var draft = BuildDraft(out var error);

        if (draft is null)
        {
            ErrorMessage = error;
            return;
        }

        IsBusy = true;
        ErrorMessage = null;

        try
        {
            await _settings.SaveAsync(draft);

            // 保存しただけでは受信中の設定は変わらない。繋ぎ直して初めて反映される。
            await _scannerHost.RestartAsync(_storeId);

            ScannerState = _scanner.State;
            IsDirty = false;

            _dialogs.ShowInfo("保存しました", "店舗設定を保存しました。");
        }
        catch (Exception ex)
        {
            // 保存できていないのに閉じると、設定したつもりの内容が消える。
            _logger.LogError(ex, "店舗設定を保存できませんでした。");
            ErrorMessage = $"保存できませんでした。{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task DiscardAsync()
    {
        if (!_dialogs.Confirm(
            "変更を破棄しますか？",
            "入力した内容を捨てて、保存済みの設定に戻します。",
            "破棄する",
            "やめる"))
        {
            return;
        }

        await LoadAsync();
    }

    private void AddAnnouncement()
    {
        if (!CanAddAnnouncement)
        {
            return;
        }

        Announcements.Add(Track(new AnnouncementEditItem()));
    }

    private void RemoveAnnouncement(AnnouncementEditItem? item)
    {
        if (item is not null)
        {
            Announcements.Remove(item);
        }
    }

    /// <summary>
    /// COM ポートを取り直す。
    ///
    /// USB のリーダーは抜き差しで名前が変わる。開いたときの一覧を持ち回すと、
    /// 挿し直したポートを選べない。
    /// </summary>
    private void RefreshPorts()
    {
        var selected = ComPort;

        AvailablePorts.Clear();

        foreach (var port in _ports.GetPortNames())
        {
            AvailablePorts.Add(port);
        }

        // 保存済みのポートが今つながっていなくても選択は残す。
        // 消すと、設定を開いただけで保存済みの値が失われる。
        if (!string.IsNullOrWhiteSpace(selected) && !AvailablePorts.Contains(selected))
        {
            AvailablePorts.Add(selected);
        }

        ComPort = selected;
    }

    private async Task ConnectAsync()
    {
        IsBusy = true;

        try
        {
            await _scannerHost.RestartAsync(_storeId);
            ScannerState = _scanner.State;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Disconnect()
    {
        _scannerHost.Stop();
        ScannerState = _scanner.State;
    }

    private async Task TestConnectionAsync()
    {
        if (string.IsNullOrWhiteSpace(ComPort))
        {
            TestResult = "接続ポートを選んでください。";
            TestSucceeded = false;
            return;
        }

        IsBusy = true;
        TestResult = $"QRコードをかざしてください。（{(int)TestTimeout.TotalSeconds}秒以内）";
        TestSucceeded = false;

        try
        {
            // ポートは排他利用。テスト中はメインの受信を止め、終わったら戻す
            // （→ CLAUDE.md 必須ルール4）。その面倒はサービス側が見る。
            var result = await _scanner.TestConnectionAsync(
                new SerialPortSettings
                {
                    PortName = ComPort,
                    BaudRate = BaudRate,
                    DataBits = DataBits,
                    Parity = Enum.Parse<System.IO.Ports.Parity>(Parity),
                    StopBits = Enum.Parse<System.IO.Ports.StopBits>(StopBits),
                },
                TestTimeout);

            TestResult = result.Message;
            TestSucceeded = result.Success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "接続テストに失敗しました。");
            TestResult = $"接続テストに失敗しました。{ex.Message}";
            TestSucceeded = false;
        }
        finally
        {
            IsBusy = false;
            ScannerState = _scanner.State;
        }
    }

    private static string Format(TimeOnly time)
        => time.ToString("HH:mm", CultureInfo.InvariantCulture);

    private static bool TryParseTime(string text, out TimeOnly time)
        => TimeOnly.TryParseExact(
            text?.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out time);

    /// <summary>お知らせの本文が変わっても未保存にする。</summary>
    private AnnouncementEditItem Track(AnnouncementEditItem item)
    {
        item.PropertyChanged += OnAnnouncementItemChanged;

        return item;
    }

    private void OnAnnouncementItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_loading)
        {
            IsDirty = true;
        }
    }

    private void OnAnnouncementsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var removed in e.OldItems?.OfType<AnnouncementEditItem>() ?? [])
        {
            removed.PropertyChanged -= OnAnnouncementItemChanged;
        }

        if (!_loading)
        {
            IsDirty = true;
        }

        NotifyAnnouncementCountChanged();
    }

    private void OnAnyPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        // 状態の表示そのものは「変更」ではない。含めると常に未保存になる。
        if (e.PropertyName is nameof(IsDirty) or nameof(IsBusy) or nameof(ErrorMessage)
            or nameof(TestResult) or nameof(TestSucceeded) or nameof(ScannerState)
            or nameof(ScannerStatusText) or nameof(StoreIdText) or nameof(CompanyName)
            or nameof(AnnouncementCountText) or nameof(CanAddAnnouncement))
        {
            return;
        }

        IsDirty = true;
    }

    private void NotifyAnnouncementCountChanged()
    {
        OnPropertyChanged(nameof(AnnouncementCountText));
        OnPropertyChanged(nameof(CanAddAnnouncement));

        AddAnnouncementCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsDirtyChanged(bool value)
    {
        SaveCommand.NotifyCanExecuteChanged();
        DiscardCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsBusyChanged(bool value)
    {
        SaveCommand.NotifyCanExecuteChanged();
        DiscardCommand.NotifyCanExecuteChanged();
        ConnectCommand.NotifyCanExecuteChanged();
        TestConnectionCommand.NotifyCanExecuteChanged();
    }

    partial void OnScannerStateChanged(ScannerState value)
        => OnPropertyChanged(nameof(ScannerStatusText));
}
