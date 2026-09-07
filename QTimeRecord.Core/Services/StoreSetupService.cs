using QTimeRecord.Core.Data.Repositories;
using QTimeRecord.Core.Domain;

namespace QTimeRecord.Core.Services;

/// <summary>初回セットアップの入力。</summary>
public sealed record StoreSetupRequest
{
    public required string CompanyName { get; init; }

    public required string StoreName { get; init; }

    public string? StoreCode { get; init; }

    /// <summary>1日の開始時刻。営業日の境界になる（→ plan.md Q2）。</summary>
    public TimeOnly BusinessDayStart { get; init; } = new(0, 0);

    /// <summary>1日の終了時刻。営業時間外の判定に使う。</summary>
    public TimeOnly BusinessDayEnd { get; init; } = new(0, 0);

    /// <summary>管理者PIN（8桁の数字）。</summary>
    public required string AdminPin { get; init; }
}

public interface IStoreSetupService
{
    /// <summary>店舗が登録済みか。未登録なら初回セットアップ画面を出す。</summary>
    Task<bool> IsInitializedAsync(CancellationToken ct = default);

    /// <summary>店舗・管理者PIN・シリアル設定の初期値を作る。</summary>
    Task<Store> InitializeAsync(StoreSetupRequest request, CancellationToken ct = default);

    /// <summary>PIN の書式を検証する。画面側の入力チェックにも使う。</summary>
    static bool IsValidPin(string? pin)
        => pin is { Length: AdminPinLength } && pin.All(char.IsAsciiDigit);

    /// <summary>管理者PIN の桁数。仕様で8桁の数字と決まっている。</summary>
    const int AdminPinLength = 8;
}

public sealed class StoreSetupService(
    IStoreRepository stores,
    IAdminCredentialRepository credentials,
    IDeviceSettingsRepository devices,
    IPasswordHasher hasher) : IStoreSetupService
{
    public Task<bool> IsInitializedAsync(CancellationToken ct = default)
        => stores.ExistsAsync(ct);

    public async Task<Store> InitializeAsync(StoreSetupRequest request, CancellationToken ct = default)
    {
        Validate(request);

        if (await stores.ExistsAsync(ct))
        {
            // 二重に実行されると店舗が2つになり、打刻の所属が壊れる。
            throw new InvalidOperationException("すでに初期化されています。");
        }

        var now = DateTime.Now;

        var store = new Store
        {
            Id = Guid.CreateVersion7(),
            CompanyName = request.CompanyName.Trim(),
            StoreName = request.StoreName.Trim(),
            StoreCode = string.IsNullOrWhiteSpace(request.StoreCode) ? null : request.StoreCode.Trim(),
            BusinessDayStart = request.BusinessDayStart,
            BusinessDayEnd = request.BusinessDayEnd,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await stores.AddAsync(store, ct);

        var hashed = hasher.Hash(request.AdminPin);

        await credentials.SaveAsync(
            new AdminCredential
            {
                StoreId = store.Id,
                PasswordHash = hashed.Hash,
                Salt = hashed.Salt,
                Iterations = hashed.Iterations,
                UpdatedAt = now,
            },
            ct);

        // シリアル設定は既定値で1行作っておく。
        // 行が無い状態を各画面で場合分けしないで済ませるため。
        await devices.SaveAsync(new DeviceSettings { StoreId = store.Id, UpdatedAt = now }, ct);

        return store;
    }

    private static void Validate(StoreSetupRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CompanyName))
        {
            throw new ArgumentException("会社名を入力してください。", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.StoreName))
        {
            throw new ArgumentException("店舗名を入力してください。", nameof(request));
        }

        if (!IStoreSetupService.IsValidPin(request.AdminPin))
        {
            throw new ArgumentException("管理者パスワードは8桁の数字で入力してください。", nameof(request));
        }
    }
}
