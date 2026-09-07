using QTimeRecord.Core.Data.Repositories;
using QTimeRecord.Core.Services;
using QTimeRecord.Tests.Data;

namespace QTimeRecord.Tests.Services;

public sealed class StoreSetupServiceTests
{
    private static readonly StoreSetupRequest ValidRequest = new()
    {
        CompanyName = "テスト株式会社",
        StoreName = "相模原店",
        StoreCode = "101",
        BusinessDayStart = new TimeOnly(11, 0),
        BusinessDayEnd = new TimeOnly(5, 0),
        AdminPin = "12345678",
    };

    [Fact]
    public async Task 空のDBは未初期化と判定される()
    {
        using var db = new TestDatabase();
        db.Migrate();

        var service = CreateService(db, out _);

        Assert.False(await service.IsInitializedAsync());
    }

    [Fact]
    public async Task 初期化で店舗と管理者PINとシリアル設定が作られる()
    {
        using var db = new TestDatabase();
        db.Migrate();

        var service = CreateService(db, out var deps);

        var store = await service.InitializeAsync(ValidRequest);

        Assert.True(await service.IsInitializedAsync());
        Assert.Equal("相模原店", store.StoreName);
        Assert.Equal(new TimeOnly(11, 0), store.BusinessDayStart);

        var credential = await deps.Credentials.GetAsync(store.Id);
        Assert.NotNull(credential);

        // PIN は平文で残さない
        Assert.DoesNotContain("12345678", credential.PasswordHash, StringComparison.Ordinal);
        Assert.True(deps.Hasher.Verify(
            "12345678", credential.PasswordHash, credential.Salt, credential.Iterations));

        // 設定行が無い状態を各画面で場合分けせずに済むよう、既定値で作っておく
        var device = await deps.Devices.GetAsync(store.Id);
        Assert.NotNull(device);
        Assert.Equal(9600, device.BaudRate);
    }

    [Fact]
    public async Task 二重に初期化するとエラーになる()
    {
        using var db = new TestDatabase();
        db.Migrate();

        var service = CreateService(db, out _);
        await service.InitializeAsync(ValidRequest);

        // 2つ目の店舗ができると、打刻の所属が壊れる
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.InitializeAsync(ValidRequest));
    }

    [Theory]
    [InlineData("1234567")]       // 7桁
    [InlineData("123456789")]     // 9桁
    [InlineData("1234567a")]      // 数字以外
    [InlineData("        ")]      // 空白
    [InlineData("")]
    public async Task 不正なPINは拒否される(string pin)
    {
        using var db = new TestDatabase();
        db.Migrate();

        var service = CreateService(db, out _);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.InitializeAsync(ValidRequest with { AdminPin = pin }));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task 店舗名と会社名は必須(string blank)
    {
        using var db = new TestDatabase();
        db.Migrate();

        var service = CreateService(db, out _);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.InitializeAsync(ValidRequest with { StoreName = blank }));

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.InitializeAsync(ValidRequest with { CompanyName = blank }));
    }

    [Theory]
    [InlineData("12345678", true)]
    [InlineData("00000000", true)]
    [InlineData("1234567", false)]
    [InlineData("abcdefgh", false)]
    [InlineData(null, false)]
    public void PINの書式判定(string? pin, bool expected)
    {
        Assert.Equal(expected, IStoreSetupService.IsValidPin(pin));
    }

    private static IStoreSetupService CreateService(TestDatabase db, out Dependencies deps)
    {
        deps = new Dependencies(
            new StoreRepository(db.Factory),
            new AdminCredentialRepository(db.Factory),
            new DeviceSettingsRepository(db.Factory),
            new PasswordHasher());

        return new StoreSetupService(deps.Stores, deps.Credentials, deps.Devices, deps.Hasher);
    }

    private sealed record Dependencies(
        IStoreRepository Stores,
        IAdminCredentialRepository Credentials,
        IDeviceSettingsRepository Devices,
        IPasswordHasher Hasher);
}
