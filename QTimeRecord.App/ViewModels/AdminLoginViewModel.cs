using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using QTimeRecord.App.Controls;
using QTimeRecord.App.Services;
using QTimeRecord.Core.Data.Repositories;
using QTimeRecord.Core.Infrastructure;
using QTimeRecord.Core.Services;

namespace QTimeRecord.App.ViewModels;

/// <summary>
/// 管理者ログイン。
///
/// 店舗の選択は置かない（1端末＝1店舗 → plan.md Q1）。モックにはあるが、
/// 選ばせると誤った店舗にログインできてしまい、確認の意味が薄れる。
/// </summary>
public sealed partial class AdminLoginViewModel : ObservableObject, IDisposable
{
    /// <summary>PIN の桁数。仕様で8桁の数字と決まっている。</summary>
    public const int PinLength = IStoreSetupService.AdminPinLength;

    /// <summary>
    /// 入力を待つ時間。
    ///
    /// この画面を出したまま人が離れると、<b>誰も打刻できない端末になる</b>
    /// （管理画面の間は打刻を受け付けないため）。待機画面へ自動で戻す。
    /// </summary>
    public static readonly TimeSpan InputTimeout = TimeSpan.FromSeconds(60);

    private readonly IAdminAuthService _auth;
    private readonly IStoreRepository _stores;
    private readonly IdleTimeoutService _timeout;
    private readonly IUiTicker _ticker;
    private readonly IClock _clock;
    private readonly ILogger<AdminLoginViewModel> _logger;

    /// <summary>ロック解除の時刻。ロックしていなければ null。</summary>
    private DateTime? _lockedUntil;

    private bool _disposed;

    public AdminLoginViewModel(
        IAdminAuthService auth,
        IStoreRepository stores,
        IdleTimeoutService timeout,
        IUiTicker ticker,
        IClock clock,
        ILogger<AdminLoginViewModel> logger)
    {
        _auth = auth;
        _stores = stores;
        _timeout = timeout;
        _ticker = ticker;
        _clock = clock;
        _logger = logger;

        SubmitCommand = new AsyncRelayCommand(SubmitAsync, () => CanSubmit);
        CancelCommand = new RelayCommand(Cancel);

        _timeout.Elapsed += OnTimeoutElapsed;
        _ticker.Tick += OnTick;
    }

    /// <summary>認証できた。呼び出し側が管理画面へ進める。</summary>
    public event EventHandler? Authenticated;

    /// <summary>キャンセルまたは時間切れ。呼び出し側が待機画面へ戻す。</summary>
    public event EventHandler? Cancelled;

    [ObservableProperty]
    private string _storeName = string.Empty;

    [ObservableProperty]
    private string _companyName = string.Empty;

    /// <summary>入力中の PIN。テンキーが書き込む。<b>画面には出さない。</b></summary>
    [ObservableProperty]
    private string _pin = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>ロック中の案内。残り時間を含む。ロックしていなければ null。</summary>
    [ObservableProperty]
    private string? _lockMessage;

    [ObservableProperty]
    private bool _isLocked;

    [ObservableProperty]
    private bool _isBusy;

    public int MaxLength => PinLength;

    /// <summary>伏せ字。キオスク端末の画面は誰からも見える。</summary>
    public string MaskedPin => PinEntry.Mask(Pin);

    /// <summary>「3 / 8 桁」。伏せ字だけだと何桁入れたか分からない。</summary>
    public string PinIndicator => PinEntry.Indicator(Pin, PinLength);

    public bool CanSubmit => !IsLocked && !IsBusy && Pin.Length == PinLength;

    public IRelayCommand SubmitCommand { get; }

    public ICommand CancelCommand { get; }

    /// <summary>店舗名とロック状態を読み込む。</summary>
    public async Task LoadAsync(CancellationToken ct = default)
    {
        var store = await _stores.GetAsync(ct);

        if (store is not null)
        {
            StoreName = store.StoreName;
            CompanyName = store.CompanyName;
        }

        ApplyStatus(await _auth.GetStatusAsync(ct));

        _timeout.Start(InputTimeout);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _timeout.Elapsed -= OnTimeoutElapsed;
        _ticker.Tick -= OnTick;
        _timeout.Dispose();
    }

    /// <summary>認証する。テストから直接呼べるよう公開する。</summary>
    public async Task SubmitAsync()
    {
        if (!CanSubmit)
        {
            return;
        }

        IsBusy = true;

        try
        {
            var outcome = await _auth.AuthenticateAsync(Pin);

            // 結果にかかわらず入力は消す。残すと、次に触った人が続きから試せる。
            Pin = string.Empty;

            if (outcome.IsSuccess)
            {
                _timeout.Stop();
                Authenticated?.Invoke(this, EventArgs.Empty);
                return;
            }

            ApplyStatus(outcome);

            // 入力し直す時間を確保する。ここで数え直さないと途中で画面が消える。
            _timeout.Reset();
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>認証結果を画面の文言に落とす。</summary>
    private void ApplyStatus(AdminAuthOutcome outcome)
    {
        IsLocked = outcome.IsLocked;

        if (outcome.IsLocked)
        {
            // 解除時刻を覚えておき、残り時間は時計から求める。
            // 毎回 DB に聞くと、100ms ごとの読み取りが5分間続くことになる。
            _lockedUntil = _clock.Now + outcome.LockRemaining;
            LockMessage = DescribeLock(outcome.LockRemaining);
            ErrorMessage = null;
            return;
        }

        _lockedUntil = null;
        LockMessage = null;

        ErrorMessage = outcome.Result switch
        {
            AdminAuthResult.InvalidPin when outcome.RemainingAttempts < AdminAuthService.MaxFailedAttempts
                => $"パスワードが違います。あと {outcome.RemainingAttempts} 回間違えると"
                    + $"{(int)AdminAuthService.LockDuration.TotalMinutes}分間ロックされます。",
            AdminAuthResult.NotConfigured
                => "管理者パスワードが登録されていません。初期設定をやり直してください。",
            _ => null,
        };
    }

    /// <summary>「あと 4分12秒 ロックされています」。</summary>
    public static string DescribeLock(TimeSpan remaining)
    {
        var seconds = Math.Max((int)Math.Ceiling(remaining.TotalSeconds), 0);

        return $"連続して間違えたため、あと {seconds / 60}分{seconds % 60:00}秒 ロックされています。";
    }

    private void Cancel()
    {
        _timeout.Stop();
        Cancelled?.Invoke(this, EventArgs.Empty);
    }

    private void OnTimeoutElapsed(object? sender, EventArgs e)
    {
        _logger.LogInformation("無操作のため管理者ログインを中止しました。");
        Cancelled?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>ロックの残り時間を数える。0 になったら入力できるようにする。</summary>
    private void OnTick(object? sender, EventArgs e)
    {
        if (_lockedUntil is not { } until)
        {
            return;
        }

        var remaining = until - _clock.Now;

        if (remaining <= TimeSpan.Zero)
        {
            _lockedUntil = null;
            IsLocked = false;
            LockMessage = null;
            return;
        }

        var message = DescribeLock(remaining);

        // 秒が変わったときだけ書き換える。100ms ごとに更新すると無駄に再描画される。
        if (message != LockMessage)
        {
            LockMessage = message;
        }
    }

    partial void OnPinChanged(string value)
    {
        OnPropertyChanged(nameof(MaskedPin));
        OnPropertyChanged(nameof(PinIndicator));
        NotifyCanSubmitChanged();

        // 入力は操作。数え直さないと、入れている途中で画面が消える。
        _timeout.Reset();
    }

    partial void OnIsLockedChanged(bool value) => NotifyCanSubmitChanged();

    partial void OnIsBusyChanged(bool value) => NotifyCanSubmitChanged();

    private void NotifyCanSubmitChanged()
    {
        OnPropertyChanged(nameof(CanSubmit));
        SubmitCommand.NotifyCanExecuteChanged();
    }
}
