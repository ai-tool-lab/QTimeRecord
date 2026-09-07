using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using QTimeRecord.App.Services;
using QTimeRecord.Core.Services;

namespace QTimeRecord.App.ViewModels;

/// <summary>
/// 初回セットアップ。
///
/// 既定のパスワードを持たせず、この画面で必ず決めさせる。
/// 初期値を固定にすると、変更されないまま運用されることが多い。
/// </summary>
public sealed partial class SetupViewModel(
    IStoreSetupService setup,
    IDialogService dialogs,
    ILogger<SetupViewModel> logger) : ObservableObject
{
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
    private string _businessDayEnd = "18:00";

    [ObservableProperty]
    private string _adminPin = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>この画面が完了したら呼ばれる。起動処理が待機画面へ切り替える。</summary>
    public event EventHandler<Guid>? Completed;

    public ICommand SubmitCommand => new AsyncRelayCommand(SubmitAsync, () => !IsBusy);

    private async Task SubmitAsync()
    {
        ErrorMessage = null;

        if (!TryParseTime(BusinessDayStart, out var start))
        {
            ErrorMessage = "1日の開始時刻を HH:mm で入力してください。";
            return;
        }

        if (!TryParseTime(BusinessDayEnd, out var end))
        {
            ErrorMessage = "1日の終了時刻を HH:mm で入力してください。";
            return;
        }

        IsBusy = true;

        try
        {
            var store = await setup.InitializeAsync(new StoreSetupRequest
            {
                CompanyName = CompanyName,
                StoreName = StoreName,
                StoreCode = StoreCode,
                BusinessDayStart = start,
                BusinessDayEnd = end,
                AdminPin = AdminPin,
            });

            logger.LogInformation("初回セットアップを完了しました。店舗ID: {StoreId}", store.Id);

            Completed?.Invoke(this, store.Id);
        }
        catch (ArgumentException ex)
        {
            // 入力の不備。画面に出して直してもらう。
            ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "初回セットアップに失敗しました。");
            dialogs.ShowError("セットアップに失敗しました", "もう一度お試しください。");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static bool TryParseTime(string value, out TimeOnly time)
        => TimeOnly.TryParseExact(value?.Trim(), "H:mm", out time)
            || TimeOnly.TryParseExact(value?.Trim(), "HH:mm", out time);
}
