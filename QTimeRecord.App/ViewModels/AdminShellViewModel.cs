using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using QTimeRecord.App.Services;
using QTimeRecord.Core.Devices;
using QTimeRecord.Core.Infrastructure;

namespace QTimeRecord.App.ViewModels;

/// <summary>
/// 管理画面のタブ1枚。
///
/// 中身は開いたときに作る。3画面ぶんを最初にすべて読み込むと、
/// 使わないタブのために DB を読むことになる。
/// </summary>
/// <param name="Title">タブの見出し。</param>
/// <param name="CreateContent">中身の ViewModel を作る。</param>
public sealed record AdminTab(string Title, Func<object> CreateContent);

/// <summary>
/// まだ作っていない画面の代わり。
/// 空白のタブを出すと「壊れている」ようにしか見えないため、状況を書いて出す。
/// </summary>
public sealed record AdminPlaceholder(string Title, string Message);

/// <summary>
/// 管理画面のシェル。タブの切り替えとログアウトだけを持つ。
///
/// <b>この画面にいる間は打刻を受け付けない</b>（→ plan.md 10-3-4）。
/// 管理者が操作している最中に別の人が打刻すると、どちらの操作なのか分からなくなる。
/// 読み取り自体は無視するが、なぜ打刻できなかったかを追えるようログには残す。
/// </summary>
public sealed partial class AdminShellViewModel : ObservableObject, IDisposable
{
    private readonly IQrScannerService _scanner;
    private readonly ILogger<AdminShellViewModel> _logger;

    private AdminTab? _currentTab;
    private bool _switching;
    private bool _disposed;

    public AdminShellViewModel(
        IQrScannerService scanner,
        IAdminTabProvider tabs,
        ILogger<AdminShellViewModel> logger)
    {
        _scanner = scanner;
        _logger = logger;

        LogoutCommand = new RelayCommand(Logout);

        Tabs = [.. tabs.Create()];

        _scanner.Scanned += OnScannedWhileAdmin;

        SelectedTab = Tabs.FirstOrDefault();
    }

    /// <summary>ログアウトした。呼び出し側が待機画面へ戻す。</summary>
    public event EventHandler? LoggedOut;

    public ObservableCollection<AdminTab> Tabs { get; }

    [ObservableProperty]
    private AdminTab? _selectedTab;

    /// <summary>選択中のタブの中身。</summary>
    [ObservableProperty]
    private object? _content;

    public ICommand LogoutCommand { get; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _scanner.Scanned -= OnScannedWhileAdmin;

        if (Content is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private void Logout()
    {
        // 未保存の設定を抱えたままログアウトすると、入力が黙って消える。
        if (!CanLeaveCurrent())
        {
            return;
        }

        _logger.LogInformation("管理画面からログアウトしました。");
        LoggedOut?.Invoke(this, EventArgs.Empty);
    }

    private bool CanLeaveCurrent()
        => Content is not IConfirmNavigation guard || guard.CanLeave();

    /// <summary>管理画面にいる間の読み取り。記録せず、起きたことだけを残す。</summary>
    private void OnScannedWhileAdmin(object? sender, string token)
        => _logger.LogInformation(
            "管理画面のため打刻を受け付けませんでした。QR={Fingerprint}",
            LogSafe.TokenFingerprint(token));

    partial void OnSelectedTabChanged(AdminTab? value)
    {
        if (_switching)
        {
            return;
        }

        if (!CanLeaveCurrent())
        {
            // 選択だけ進むと、中身と見た目がずれる。押す前の状態へ戻す。
            _switching = true;
            SelectedTab = _currentTab;
            _switching = false;
            return;
        }

        // 前のタブの購読やタイマーを残さない。
        if (Content is IDisposable disposable)
        {
            disposable.Dispose();
        }

        _currentTab = value;
        Content = value?.CreateContent();
    }
}
