using Microsoft.Extensions.DependencyInjection;
using QTimeRecord.Core.Data;
using QTimeRecord.Core.Data.Repositories;
using QTimeRecord.Core.Devices;
using QTimeRecord.Core.Infrastructure;
using QTimeRecord.Core.Services;

namespace QTimeRecord.Core;

/// <summary>
/// Core の DI 登録。App からはこの1つを呼ぶ。
/// </summary>
public static class CoreRegistration
{
    public static IServiceCollection AddQTimeRecordCore(
        this IServiceCollection services, AppSettings settings)
    {
        var paths = new AppPaths(settings);

        services.AddSingleton(paths);
        services.AddSingleton<IClock, SystemClock>();

        // Core は ILogger を使う。ホスト側が入れてくれる前提にせず、ここで確保しておく。
        services.AddQTimeRecordLogging(paths, settings);

        services.AddQTimeRecordDatabase(settings);

        services.AddSingleton<IDatabaseInitializer, DatabaseInitializer>();

        // Repository は状態を持たない。DbContext は都度ファクトリから作るため単一で問題ない。
        services.AddSingleton<IStoreRepository, StoreRepository>();
        services.AddSingleton<IStaffRepository, StaffRepository>();
        services.AddSingleton<IQrTokenRepository, QrTokenRepository>();
        services.AddSingleton<ITimeRecordRepository, TimeRecordRepository>();
        services.AddSingleton<IDeviceSettingsRepository, DeviceSettingsRepository>();
        services.AddSingleton<IAdminCredentialRepository, AdminCredentialRepository>();

        services.AddSingleton<ISerialPortFactory, SystemSerialPortFactory>();
        services.AddSingleton<ISerialPortEnumerator, SystemSerialPortEnumerator>();
        services.AddSingleton<IQrScannerService, QrScannerService>();
        services.AddSingleton<IQrTokenService, QrTokenService>();
        services.AddSingleton<IQrCodeImageGenerator, QrCodeImageGenerator>();

        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddSingleton<IStoreSetupService, StoreSetupService>();
        services.AddSingleton<IPunchService, PunchService>();
        services.AddSingleton<IAdminAuthService, AdminAuthService>();
        services.AddSingleton<IAttendanceQueryService, AttendanceQueryService>();
        services.AddSingleton<ITimeRecordEditService, TimeRecordEditService>();
        services.AddSingleton<IStaffService, StaffService>();
        services.AddSingleton<IStoreSettingsService, StoreSettingsService>();
        services.AddSingleton<ICsvExportService, CsvExportService>();
        services.AddSingleton<IUserErrorPresenter, UserErrorPresenter>();

        return services;
    }
}
