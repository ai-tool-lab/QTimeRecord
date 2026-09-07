using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using QTimeRecord.App.Services;
using QTimeRecord.Core.Data.Repositories;
using QTimeRecord.Core.Devices;
using QTimeRecord.Core.Domain;
using QTimeRecord.Core.Infrastructure;
using QTimeRecord.Core.Services;

namespace QTimeRecord.App.ViewModels;

/// <summary>メイン画面に出すお知らせ1件。</summary>
public sealed record AnnouncementItem(string Heading, string Body);

/// <summary>
/// 待機画面。QR の読み取りを待つ、通常時に表示され続ける画面。
/// </summary>
public sealed partial class IdleViewModel : ObservableObject, IDisposable
{
    /// <summary>エラーを表示しておく時間。読んで理解できる程度に置き、自動で消す。</summary>
    public static readonly TimeSpan ErrorDisplayDuration = TimeSpan.FromSeconds(4);

    private readonly IStoreRepository _stores;
    private readonly IQrScannerService _scanner;
    private readonly IQrTokenService _qrTokens;
    private readonly IClock _clock;
    private readonly IUiTicker _ticker;
    private readonly ILogger<IdleViewModel> _logger;

    private DateTime? _errorShownAt;
    private bool _disposed;

    public IdleViewModel(
        IStoreRepository stores,
        IQrScannerService scanner,
        IQrTokenService qrTokens,
        IClock clock,
        IUiTicker ticker,
        ILogger<IdleViewModel> logger)
    {
        _stores = stores;
        _scanner = scanner;
        _qrTokens = qrTokens;
        _clock = clock;
        _ticker = ticker;
        _logger = logger;

        _now = clock.Now;
        _scannerState = scanner.State;

        _scanner.Scanned += OnScanned;
        _scanner.ScanFailed += OnScanFailed;
        _scanner.StateChanged += OnScannerStateChanged;
        _ticker.Tick += OnTick;
    }

    /// <summary>有効な QR を読み取った。打刻画面への遷移は購読側が行う（→ 9章）。</summary>
    public event EventHandler<Staff>? StaffIdentified;

    /// <summary>管理画面が要求された。遷移は購読側が行う（→ 10章）。</summary>
    public event EventHandler? AdminRequested;

    public ICommand AdminLoginCommand => new RelayCommand(
        () => AdminRequested?.Invoke(this, EventArgs.Empty));

    [ObservableProperty]
    private string _companyName = string.Empty;

    [ObservableProperty]
    private string _storeName = string.Empty;

    [ObservableProperty]
    private string? _announcementTitle;

    [ObservableProperty]
    private DateTime _now;

    [ObservableProperty]
    private ScannerState _scannerState;

    /// <summary>読み取りに失敗したときの案内。表示していないときは null。</summary>
    [ObservableProperty]
    private string? _errorMessage;

    public ObservableCollection<AnnouncementItem> Announcements { get; } = [];

    public string ScannerStatusText => ScannerState switch
    {
        ScannerState.Connected => "リーダー待機中",
        ScannerState.Disconnected => "リーダー未接続",
        _ => "停止中",
    };

    public bool IsScannerReady => ScannerState == ScannerState.Connected;

    /// <summary>
    /// リーダーが繋がっていないときの案内。繋がっていれば null。
    ///
    /// 状態を色の点だけで示すと、かざしても反応しない理由が伝わらない
    /// （→ plan.md 15-1）。
    /// </summary>
    public string? ScannerNotice => ScannerState == ScannerState.Connected
        ? null
        : "QRリーダーとの接続が切れました。再接続しています。";

    public bool HasAnnouncements => Announcements.Count > 0;

    /// <summary>店舗情報とお知らせを読み込む。</summary>
    public async Task LoadAsync(CancellationToken ct = default)
    {
        var store = await _stores.GetAsync(ct);

        if (store is null)
        {
            _logger.LogWarning("店舗が登録されていません。");
            return;
        }

        CompanyName = store.CompanyName;
        StoreName = store.StoreName;
        AnnouncementTitle = store.AnnouncementTitle;

        Announcements.Clear();

        foreach (var announcement in await _stores.GetAnnouncementsAsync(store.Id, ct))
        {
            Announcements.Add(new AnnouncementItem(announcement.Heading, announcement.Body));
        }

        OnPropertyChanged(nameof(HasAnnouncements));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // 購読を残すと、画面を離れたあとも読み取りに反応してしまう。
        _scanner.Scanned -= OnScanned;
        _scanner.ScanFailed -= OnScanFailed;
        _scanner.StateChanged -= OnScannerStateChanged;
        _ticker.Tick -= OnTick;
    }

    /// <summary>読み取った QR を判定する。テストから直接呼べるよう公開する。</summary>
    public async Task HandleScanAsync(string token, CancellationToken ct = default)
    {
        var result = await _qrTokens.ResolveAsync(token, ct);

        if (result.CanPunch && result.Staff is not null)
        {
            ErrorMessage = null;
            _logger.LogInformation("スタッフを特定しました: {StaffId}", result.Staff.Id);

            StaffIdentified?.Invoke(this, result.Staff);
            return;
        }

        LogFailure(token, result);
        ShowError(DescribeFailure(result));
    }

    /// <summary>
    /// 読み取れなかった理由をログに残す（→ plan.md 15-1）。
    ///
    /// 残さないと「かざしても打刻できない」という申告に対して、
    /// 何が起きたのかを後から確かめる手段が無くなる。
    /// <b>氏名とトークンは書かない</b>（→ CLAUDE.md 必須ルール6）。
    /// </summary>
    private void LogFailure(string token, QrResolveResult result)
    {
        switch (result.Resolution)
        {
            case QrResolution.Revoked:
                _logger.LogWarning(
                    "失効した QR が読み取られました: {StaffId} 失効 {RevokedAt}",
                    result.StaffId,
                    result.RevokedAt);
                break;

            case QrResolution.OnLeave:
            case QrResolution.Retired:
                _logger.LogWarning(
                    "打刻できない在籍状態のため拒否しました: {StaffId} {Resolution}",
                    result.Staff?.Id ?? result.StaffId,
                    result.Resolution);
                break;

            default:
                _logger.LogWarning(
                    "登録されていない QR が読み取られました: {Fingerprint}",
                    LogSafe.TokenFingerprint(token));
                break;
        }
    }

    /// <summary>
    /// 読み取り失敗の案内。
    ///
    /// 「登録されていない」と「無効になった」は原因も対処も違う。
    /// まとめて「読み取れません」にすると、スタッフが何をすればよいか分からない。
    /// </summary>
    public static string DescribeFailure(QrResolveResult result) => result.Resolution switch
    {
        QrResolution.NotFound => "登録されていないQRコードです。管理者へ連絡してください。",
        QrResolution.Revoked => "このQRコードは無効です。新しいカードを管理者から受け取ってください。",
        QrResolution.OnLeave => "休職中のため打刻できません。管理者へ連絡してください。",
        QrResolution.Retired => "退職済みのため打刻できません。管理者へ連絡してください。",
        _ => "読み取れませんでした。もう一度かざしてください。",
    };

    private void OnScanned(object? sender, string token)
    {
        // 受信はシリアルのスレッドから来る。UI の更新は待たせずに投げておく。
        _ = HandleScanAsync(token);
    }

    /// <summary>
    /// 受信はあったが読み取れなかった。
    /// 何も出さないと、かざした人は端末が壊れていると受け取る（→ plan.md 15-1）。
    /// </summary>
    private void OnScanFailed(object? sender, EventArgs e)
        => ShowError("読み取れませんでした。もう一度かざしてください。");

    private void OnScannerStateChanged(object? sender, ScannerState state)
    {
        ScannerState = state;
        OnPropertyChanged(nameof(ScannerStatusText));
        OnPropertyChanged(nameof(IsScannerReady));
        OnPropertyChanged(nameof(ScannerNotice));
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var now = _clock.Now;

        // 秒が変わったときだけ通知する。100ms ごとに更新すると無駄に再描画される。
        if (now.Second != Now.Second || now.Date != Now.Date)
        {
            Now = now;
        }

        if (_errorShownAt is not null && now - _errorShownAt.Value >= ErrorDisplayDuration)
        {
            _errorShownAt = null;
            ErrorMessage = null;
        }
    }

    private void ShowError(string message)
    {
        ErrorMessage = message;
        _errorShownAt = _clock.Now;
    }
}
