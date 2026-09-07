using Microsoft.Extensions.Logging;
using QTimeRecord.Core.Data.Repositories;
using QTimeRecord.Core.Domain;

namespace QTimeRecord.Core.Services;

/// <summary>お知らせ1件の入力。</summary>
/// <param name="Heading">見出し（例: 健康診断）。</param>
/// <param name="Body">本文。</param>
public sealed record AnnouncementDraft(string Heading, string Body);

/// <summary>店舗設定の入力。画面の1回の保存でまとめて渡す。</summary>
public sealed record StoreSettingsDraft
{
    public required string StoreName { get; init; }

    public string? StoreCode { get; init; }

    /// <summary>1日の開始時刻。営業日の境界になる（→ plan.md Q2）。</summary>
    public required TimeOnly BusinessDayStart { get; init; }

    /// <summary>1日の終了時刻。営業時間外の判定に使う。</summary>
    public required TimeOnly BusinessDayEnd { get; init; }

    /// <summary>お知らせの件名。</summary>
    public string? AnnouncementTitle { get; init; }

    public IReadOnlyList<AnnouncementDraft> Announcements { get; init; } = [];

    public string? ComPort { get; init; }

    public int BaudRate { get; init; } = 9600;

    public int DataBits { get; init; } = 8;

    public string Parity { get; init; } = "None";

    public string StopBits { get; init; } = "One";
}

/// <summary>店舗設定の現在値。画面を開いたときに読み込む。</summary>
/// <param name="Store">店舗。</param>
/// <param name="Announcements">お知らせ（表示順）。</param>
/// <param name="Device">リーダーの接続設定。</param>
public sealed record StoreSettingsSnapshot(
    Store Store, IReadOnlyList<Announcement> Announcements, DeviceSettings Device);

public interface IStoreSettingsService
{
    /// <summary>現在の設定を読み込む。未セットアップなら null。</summary>
    Task<StoreSettingsSnapshot?> GetAsync(CancellationToken ct = default);

    /// <summary>保存前の検証。画面はこれを見てエラーを出す。</summary>
    string? Validate(StoreSettingsDraft draft);

    /// <summary>保存する。店舗・お知らせ・リーダー設定をまとめて更新する。</summary>
    Task SaveAsync(StoreSettingsDraft draft, CancellationToken ct = default);
}

/// <summary>
/// 店舗設定の読み書き。
///
/// <b>設定はすべて SQLite に置く</b>（→ plan.md 13-1）。
/// 一部を設定ファイルに置くと「設定だけ古い」状態が生まれ、
/// バックアップの経路も二重になる。
/// </summary>
public sealed class StoreSettingsService(
    IStoreRepository stores,
    IDeviceSettingsRepository devices,
    ILogger<StoreSettingsService> logger) : IStoreSettingsService
{
    /// <summary>お知らせの件数上限（→ plan.md Q4）。</summary>
    public const int MaxAnnouncements = 5;

    /// <summary>件名の文字数上限。</summary>
    public const int MaxTitleLength = 40;

    /// <summary>見出しの文字数上限。</summary>
    public const int MaxHeadingLength = 30;

    /// <summary>本文の文字数上限。</summary>
    public const int MaxBodyLength = 200;

    public async Task<StoreSettingsSnapshot?> GetAsync(CancellationToken ct = default)
    {
        var store = await stores.GetAsync(ct);

        if (store is null)
        {
            return null;
        }

        var announcements = await stores.GetAnnouncementsAsync(store.Id, ct);

        // シリアル設定はセットアップ時に既定値で1行作る。無ければ既定値で埋める。
        var device = await devices.GetAsync(store.Id, ct)
            ?? new DeviceSettings { StoreId = store.Id };

        return new StoreSettingsSnapshot(store, announcements, device);
    }

    public string? Validate(StoreSettingsDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        if (string.IsNullOrWhiteSpace(draft.StoreName))
        {
            return "店舗名を入力してください。";
        }

        if (draft.AnnouncementTitle?.Length > MaxTitleLength)
        {
            return $"お知らせの件名は{MaxTitleLength}文字までです。";
        }

        if (draft.Announcements.Count > MaxAnnouncements)
        {
            return $"お知らせは最大{MaxAnnouncements}件までです。";
        }

        foreach (var announcement in draft.Announcements)
        {
            if (string.IsNullOrWhiteSpace(announcement.Heading))
            {
                return "お知らせの見出しを入力してください。";
            }

            if (announcement.Heading.Length > MaxHeadingLength)
            {
                return $"お知らせの見出しは{MaxHeadingLength}文字までです。";
            }

            if (announcement.Body.Length > MaxBodyLength)
            {
                return $"お知らせの本文は{MaxBodyLength}文字までです。";
            }
        }

        return null;
    }

    public async Task SaveAsync(StoreSettingsDraft draft, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        if (Validate(draft) is { } error)
        {
            throw new ArgumentException(error, nameof(draft));
        }

        var store = await stores.GetAsync(ct)
            ?? throw new InvalidOperationException("店舗が登録されていません。");

        store.StoreName = draft.StoreName.Trim();
        store.StoreCode = Normalize(draft.StoreCode);
        store.BusinessDayStart = draft.BusinessDayStart;
        store.BusinessDayEnd = draft.BusinessDayEnd;
        store.AnnouncementTitle = Normalize(draft.AnnouncementTitle);

        await stores.UpdateAsync(store, ct);

        var now = DateTime.Now;

        await stores.ReplaceAnnouncementsAsync(
            store.Id,
            [.. draft.Announcements.Select((a, index) => new Announcement
            {
                Id = Guid.CreateVersion7(),
                StoreId = store.Id,
                DisplayOrder = index + 1,
                Heading = a.Heading.Trim(),
                Body = a.Body.Trim(),
                CreatedAt = now,
                UpdatedAt = now,
            })],
            ct);

        await devices.SaveAsync(
            new DeviceSettings
            {
                StoreId = store.Id,
                ComPort = Normalize(draft.ComPort),
                BaudRate = draft.BaudRate,
                DataBits = draft.DataBits,
                Parity = draft.Parity,
                StopBits = draft.StopBits,
                UpdatedAt = now,
            },
            ct);

        // 営業日の境界が変わると、以後の打刻の所属日が変わる。追えるように残す。
        logger.LogInformation(
            "店舗設定を保存しました。営業日 {Start}〜{End} / お知らせ {Count}件",
            draft.BusinessDayStart,
            draft.BusinessDayEnd,
            draft.Announcements.Count);
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
