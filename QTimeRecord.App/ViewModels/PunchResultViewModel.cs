using CommunityToolkit.Mvvm.ComponentModel;
using QTimeRecord.App.Services;
using QTimeRecord.Core.Domain;
using QTimeRecord.Core.Services;

namespace QTimeRecord.App.ViewModels;

/// <summary>
/// 打刻完了の表示。
///
/// <b>成功したことが一目で分かる画面にする。</b>
/// 打刻できたか分からないと、スタッフはもう一度かざし、二重打刻の原因になる。
/// 数秒だけ出して自動で待機画面へ戻る（誰も「閉じる」を押さない）。
/// </summary>
public sealed partial class PunchResultViewModel : ObservableObject, IDisposable
{
    private readonly IdleTimeoutService _timeout;
    private bool _disposed;

    public PunchResultViewModel(IdleTimeoutService timeout)
    {
        _timeout = timeout;
        _timeout.Elapsed += OnElapsed;
    }

    /// <summary>表示時間が過ぎた。呼び出し側が待機画面へ戻す。</summary>
    public event EventHandler? Finished;

    [ObservableProperty]
    private string _staffName = string.Empty;

    /// <summary>「出勤を記録しました」。</summary>
    [ObservableProperty]
    private string _headline = string.Empty;

    /// <summary>打刻種別の表示名。</summary>
    [ObservableProperty]
    private string _recordTypeLabel = string.Empty;

    [ObservableProperty]
    private DateTime _recordedAt;

    /// <summary>営業時間外の打刻だったか。要確認であることをその場で伝える。</summary>
    [ObservableProperty]
    private bool _isOutsideBusinessHours;

    public string OutsideBusinessHoursNote =>
        "営業時間外の打刻として記録しました。管理者の確認対象になります。";

    /// <summary>結果を流し込んで、自動で戻るまでの計測を始める。</summary>
    public void Show(Staff staff, TimeRecord record)
    {
        ArgumentNullException.ThrowIfNull(staff);
        ArgumentNullException.ThrowIfNull(record);

        StaffName = staff.Name;
        RecordTypeLabel = PunchService.Describe(record.RecordType);
        Headline = $"{RecordTypeLabel}を記録しました";
        RecordedAt = record.RecordedAt;
        IsOutsideBusinessHours = record.IsOutsideBusinessHours;

        _timeout.Start(IdleTimeoutService.ResultTimeout);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _timeout.Elapsed -= OnElapsed;
        _timeout.Dispose();
    }

    private void OnElapsed(object? sender, EventArgs e) => Finished?.Invoke(this, EventArgs.Empty);
}
