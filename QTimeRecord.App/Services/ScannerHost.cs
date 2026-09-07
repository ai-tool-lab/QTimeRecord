using Microsoft.Extensions.Logging;
using QTimeRecord.Core.Data.Repositories;
using QTimeRecord.Core.Devices;

namespace QTimeRecord.App.Services;

/// <summary>
/// QR リーダーの受信をアプリ全体で1つだけ動かす。
///
/// 画面ごとに接続すると、遷移のたびにポートの開閉が起き、
/// 排他利用のシリアルポートでは高い確率で失敗する。
/// 接続は起動から終了まで維持し、画面側は <see cref="IQrScannerService.Scanned"/> を購読するだけにする。
/// </summary>
public sealed class ScannerHost(
    IQrScannerService scanner,
    IDeviceSettingsRepository deviceSettings,
    IUiTicker ticker,
    ILogger<ScannerHost> logger) : IDisposable
{
    private bool _started;

    public async Task StartAsync(Guid storeId, CancellationToken ct = default)
    {
        if (_started)
        {
            return;
        }

        _started = true;
        ticker.Tick += OnTick;

        var settings = await deviceSettings.GetAsync(storeId, ct);

        if (settings?.ComPort is null)
        {
            // 未設定でも起動は続ける。店舗設定から COM ポートを選べば繋がる。
            logger.LogWarning("COM ポートが未設定です。店舗設定から選択してください。");
            return;
        }

        scanner.Start(SerialPortSettings.From(settings));
    }

    /// <summary>受信を止める。店舗設定から明示的に切断するときに使う。</summary>
    public void Stop() => scanner.Stop();

    /// <summary>店舗設定を変更したあと、新しい設定で繋ぎ直す。</summary>
    public async Task RestartAsync(Guid storeId, CancellationToken ct = default)
    {
        scanner.Stop();

        var settings = await deviceSettings.GetAsync(storeId, ct);

        if (settings?.ComPort is not null)
        {
            scanner.Start(SerialPortSettings.From(settings));
        }
    }

    public void Dispose()
    {
        ticker.Tick -= OnTick;
        scanner.Dispose();
    }

    private void OnTick(object? sender, EventArgs e) => scanner.Tick();
}
