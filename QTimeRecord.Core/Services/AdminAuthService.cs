using Microsoft.Extensions.Logging;
using QTimeRecord.Core.Data.Repositories;
using QTimeRecord.Core.Domain;
using QTimeRecord.Core.Infrastructure;

namespace QTimeRecord.Core.Services;

/// <summary>管理者認証の結果。</summary>
public enum AdminAuthResult
{
    /// <summary>認証できた。</summary>
    Success,

    /// <summary>PIN が違う。</summary>
    InvalidPin,

    /// <summary>連続失敗でロック中。入力させない。</summary>
    Locked,

    /// <summary>管理者PIN が登録されていない。セットアップが完了していない。</summary>
    NotConfigured,
}

/// <summary>認証の結果と、画面に出す情報。</summary>
/// <param name="Result">結果。</param>
/// <param name="RemainingAttempts">ロックまでの残り回数。ロック中は 0。</param>
/// <param name="LockRemaining">ロック解除までの残り時間。ロックしていなければ <see cref="TimeSpan.Zero"/>。</param>
public sealed record AdminAuthOutcome(
    AdminAuthResult Result, int RemainingAttempts, TimeSpan LockRemaining)
{
    public bool IsSuccess => Result == AdminAuthResult.Success;

    public bool IsLocked => Result == AdminAuthResult.Locked;
}

public interface IAdminAuthService
{
    /// <summary>PIN を検証する。失敗回数とロックはこの中で更新する。</summary>
    Task<AdminAuthOutcome> AuthenticateAsync(string pin, CancellationToken ct = default);

    /// <summary>いまロックされているか。ログイン画面を開いたときに確かめる。</summary>
    Task<AdminAuthOutcome> GetStatusAsync(CancellationToken ct = default);
}

/// <summary>
/// 管理者認証。
///
/// 8桁数字は1億通りしかない。総当たりへの実質的な備えは**連続失敗のロック**であり、
/// ハッシュの強度ではない（→ plan.md Q10 / <see cref="PasswordHasher"/>）。
///
/// <b>ロック状態は DB に置く。</b>メモリに置くと、アプリを再起動するだけで回避できる。
/// </summary>
public sealed class AdminAuthService(
    IStoreRepository stores,
    IAdminCredentialRepository credentials,
    IPasswordHasher hasher,
    IClock clock,
    ILogger<AdminAuthService> logger) : IAdminAuthService
{
    /// <summary>ロックまでの連続失敗回数。</summary>
    public const int MaxFailedAttempts = 5;

    /// <summary>ロックの長さ。</summary>
    public static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(5);

    public async Task<AdminAuthOutcome> GetStatusAsync(CancellationToken ct = default)
    {
        var credential = await LoadAsync(ct);

        if (credential is null)
        {
            return NotConfigured;
        }

        var lockRemaining = RemainingLock(credential);

        return lockRemaining > TimeSpan.Zero
            ? new AdminAuthOutcome(AdminAuthResult.Locked, 0, lockRemaining)
            : new AdminAuthOutcome(
                AdminAuthResult.InvalidPin, RemainingAttempts(credential), TimeSpan.Zero);
    }

    public async Task<AdminAuthOutcome> AuthenticateAsync(string pin, CancellationToken ct = default)
    {
        var credential = await LoadAsync(ct);

        if (credential is null)
        {
            logger.LogWarning("管理者PIN が登録されていません。");
            return NotConfigured;
        }

        var lockRemaining = RemainingLock(credential);

        if (lockRemaining > TimeSpan.Zero)
        {
            // ロック中は検証すらしない。試行を数えさせない。
            logger.LogWarning("ロック中の認証要求を拒否しました。残り {Seconds} 秒", (int)lockRemaining.TotalSeconds);
            return new AdminAuthOutcome(AdminAuthResult.Locked, 0, lockRemaining);
        }

        // ロックが明けていたら、連続失敗の数え直しから始める。
        // 引き継ぐと、ロック明けの1回の入力ミスで再びロックされる。
        if (credential.LockedUntil is not null)
        {
            credential.LockedUntil = null;
            credential.FailedCount = 0;
        }

        if (hasher.Verify(pin, credential.PasswordHash, credential.Salt, credential.Iterations))
        {
            credential.FailedCount = 0;
            credential.LockedUntil = null;

            await credentials.SaveAsync(credential, ct);

            logger.LogInformation("管理者が認証されました。");

            return new AdminAuthOutcome(AdminAuthResult.Success, MaxFailedAttempts, TimeSpan.Zero);
        }

        credential.FailedCount++;

        if (credential.FailedCount >= MaxFailedAttempts)
        {
            credential.LockedUntil = clock.Now + LockDuration;

            await credentials.SaveAsync(credential, ct);

            logger.LogWarning(
                "認証の連続失敗が {Count} 回に達したため {Minutes} 分ロックします。",
                credential.FailedCount, (int)LockDuration.TotalMinutes);

            return new AdminAuthOutcome(AdminAuthResult.Locked, 0, LockDuration);
        }

        await credentials.SaveAsync(credential, ct);

        // PIN そのものも桁数も書かない（→ CLAUDE.md 必須ルール6）。
        logger.LogWarning("認証に失敗しました。連続失敗 {Count} 回", credential.FailedCount);

        return new AdminAuthOutcome(
            AdminAuthResult.InvalidPin, RemainingAttempts(credential), TimeSpan.Zero);
    }

    private static AdminAuthOutcome NotConfigured
        => new(AdminAuthResult.NotConfigured, 0, TimeSpan.Zero);

    private static int RemainingAttempts(AdminCredential credential)
        => Math.Max(MaxFailedAttempts - credential.FailedCount, 0);

    private async Task<AdminCredential?> LoadAsync(CancellationToken ct)
    {
        // 1端末＝1店舗のため、店舗を特定する入力は要らない（→ plan.md Q1）。
        var store = await stores.GetAsync(ct);

        return store is null ? null : await credentials.GetAsync(store.Id, ct);
    }

    private TimeSpan RemainingLock(AdminCredential credential)
    {
        if (credential.LockedUntil is not { } until)
        {
            return TimeSpan.Zero;
        }

        var remaining = until - clock.Now;

        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }
}
