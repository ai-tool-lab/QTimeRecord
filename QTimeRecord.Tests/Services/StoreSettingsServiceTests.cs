using Microsoft.Extensions.Logging.Abstractions;
using QTimeRecord.Core.Data.Repositories;
using QTimeRecord.Core.Domain;
using QTimeRecord.Core.Services;
using QTimeRecord.Tests.Data;

namespace QTimeRecord.Tests.Services;

public sealed class StoreSettingsServiceTests
{
    [Fact]
    public async Task 現在の設定を読み込める()
    {
        using var db = new TestDatabase();
        var fixture = await Fixture.CreateAsync(db);

        var snapshot = await fixture.Service.GetAsync();

        Assert.NotNull(snapshot);
        Assert.Equal("相模原店", snapshot.Store.StoreName);
        Assert.Equal("テスト株式会社", snapshot.Store.CompanyName);
        Assert.Empty(snapshot.Announcements);

        // セットアップで既定値の行を作ってあるため、null にはならない。
        Assert.Equal(9600, snapshot.Device.BaudRate);
    }

    [Fact]
    public async Task 未セットアップならnullを返す()
    {
        using var db = new TestDatabase();
        db.Migrate();

        var service = Fixture.NewService(db);

        Assert.Null(await service.GetAsync());
    }

    [Fact]
    public async Task 店舗名と営業日を保存できる()
    {
        using var db = new TestDatabase();
        var fixture = await Fixture.CreateAsync(db);

        await fixture.Service.SaveAsync(fixture.Draft() with
        {
            StoreName = "町田店",
            StoreCode = "202",
            BusinessDayStart = new TimeOnly(11, 0),
            BusinessDayEnd = new TimeOnly(5, 0),
        });

        var snapshot = await fixture.Service.GetAsync();

        Assert.NotNull(snapshot);
        Assert.Equal("町田店", snapshot.Store.StoreName);
        Assert.Equal("202", snapshot.Store.StoreCode);
        Assert.Equal(new TimeOnly(11, 0), snapshot.Store.BusinessDayStart);
        Assert.Equal(new TimeOnly(5, 0), snapshot.Store.BusinessDayEnd);
    }

    [Fact]
    public async Task 営業日を変えても確定済みの打刻は動かない()
    {
        using var db = new TestDatabase();
        var fixture = await Fixture.CreateAsync(db);

        var record = new TimeRecord
        {
            Id = Guid.CreateVersion7(),
            StoreId = fixture.StoreId,
            StaffId = await fixture.AddStaffAsync(),
            RecordType = TimeRecordType.ClockIn,
            RecordedAt = new DateTime(2026, 9, 6, 2, 0, 0),
            WorkDate = new DateOnly(2026, 9, 5),
            EntryMethod = EntryMethod.Qr,
        };

        await new TimeRecordRepository(db.Factory).AddAsync(record);

        await fixture.Service.SaveAsync(fixture.Draft() with
        {
            BusinessDayStart = new TimeOnly(4, 0),
            BusinessDayEnd = new TimeOnly(23, 0),
        });

        // work_date は保存済みの値。都度計算にすると、設定を変えた瞬間に
        // 確定済みの過去月の勤怠が書き換わる（→ CLAUDE.md 必須ルール1）。
        using var separate = db.CreateSeparateContext();
        Assert.Equal(new DateOnly(2026, 9, 5), Assert.Single(separate.TimeRecords).WorkDate);
    }

    [Fact]
    public async Task お知らせを表示順で保存できる()
    {
        using var db = new TestDatabase();
        var fixture = await Fixture.CreateAsync(db);

        await fixture.Service.SaveAsync(fixture.Draft() with
        {
            AnnouncementTitle = "【店舗連絡】今週の重要共有事項",
            Announcements =
            [
                new("健康診断", "受診希望日を月末までに提出願います。"),
                new("衛生管理", "検温と手指アルコール消毒を励行してください。"),
            ],
        });

        var snapshot = await fixture.Service.GetAsync();

        Assert.NotNull(snapshot);
        Assert.Equal("【店舗連絡】今週の重要共有事項", snapshot.Store.AnnouncementTitle);
        Assert.Equal(2, snapshot.Announcements.Count);
        Assert.Equal("健康診断", snapshot.Announcements[0].Heading);
        Assert.Equal(1, snapshot.Announcements[0].DisplayOrder);
        Assert.Equal(2, snapshot.Announcements[1].DisplayOrder);
    }

    [Fact]
    public async Task 保存し直すと古いお知らせは残らない()
    {
        using var db = new TestDatabase();
        var fixture = await Fixture.CreateAsync(db);

        await fixture.Service.SaveAsync(fixture.Draft() with
        {
            Announcements = [new("健康診断", "本文A"), new("衛生管理", "本文B")],
        });

        await fixture.Service.SaveAsync(fixture.Draft() with
        {
            Announcements = [new("シフト", "本文C")],
        });

        // 消したはずのお知らせが待機画面に出続けてはいけない。
        var snapshot = await fixture.Service.GetAsync();
        Assert.Equal("シフト", Assert.Single(snapshot!.Announcements).Heading);
    }

    [Fact]
    public void 六件目のお知らせは弾く()
    {
        var draft = SampleDraft() with
        {
            Announcements = [.. Enumerable.Range(1, 6).Select(i => new AnnouncementDraft($"見出し{i}", "本文"))],
        };

        Assert.NotNull(NewService().Validate(draft));
    }

    [Fact]
    public void 五件までは通る()
    {
        var draft = SampleDraft() with
        {
            Announcements = [.. Enumerable.Range(1, 5).Select(i => new AnnouncementDraft($"見出し{i}", "本文"))],
        };

        Assert.Null(NewService().Validate(draft));
    }

    [Theory]
    [InlineData(40, true)]
    [InlineData(41, false)]
    public void 件名は四十文字までとする(int length, bool valid)
    {
        var draft = SampleDraft() with { AnnouncementTitle = new string('あ', length) };

        Assert.Equal(valid, NewService().Validate(draft) is null);
    }

    [Fact]
    public void 店舗名が空なら保存できない()
    {
        Assert.NotNull(NewService().Validate(SampleDraft() with { StoreName = "   " }));
    }

    [Fact]
    public void 見出しが空のお知らせは弾く()
    {
        var draft = SampleDraft() with { Announcements = [new("   ", "本文")] };

        // 見出しの無いカードが待機画面に出ると、本文だけが浮いて見える。
        Assert.NotNull(NewService().Validate(draft));
    }

    [Fact]
    public async Task 検証で弾かれる内容は保存しない()
    {
        using var db = new TestDatabase();
        var fixture = await Fixture.CreateAsync(db);

        await Assert.ThrowsAsync<ArgumentException>(
            () => fixture.Service.SaveAsync(fixture.Draft() with { StoreName = "  " }));

        // 保存に失敗しても、DB の値が半分だけ変わってはいけない。
        var snapshot = await fixture.Service.GetAsync();
        Assert.Equal("相模原店", snapshot!.Store.StoreName);
    }

    [Fact]
    public async Task リーダー設定を保存できる()
    {
        using var db = new TestDatabase();
        var fixture = await Fixture.CreateAsync(db);

        await fixture.Service.SaveAsync(fixture.Draft() with
        {
            ComPort = "COM3",
            BaudRate = 115200,
            DataBits = 7,
            Parity = "Even",
            StopBits = "Two",
        });

        var snapshot = await fixture.Service.GetAsync();

        Assert.NotNull(snapshot);
        Assert.Equal("COM3", snapshot.Device.ComPort);
        Assert.Equal(115200, snapshot.Device.BaudRate);
        Assert.Equal("Even", snapshot.Device.Parity);
    }

    [Fact]
    public async Task 保存したリーダー設定がそのまま接続に使える()
    {
        using var db = new TestDatabase();
        var fixture = await Fixture.CreateAsync(db);

        await fixture.Service.SaveAsync(fixture.Draft() with
        {
            ComPort = "COM5",
            BaudRate = 19200,
            Parity = "Odd",
            StopBits = "Two",
        });

        var snapshot = await fixture.Service.GetAsync();
        var settings = Core.Devices.SerialPortSettings.From(snapshot!.Device);

        Assert.True(settings.HasPort);
        Assert.Equal("COM5", settings.PortName);
        Assert.Equal(19200, settings.BaudRate);
        Assert.Equal(System.IO.Ports.Parity.Odd, settings.Parity);
        Assert.Equal(System.IO.Ports.StopBits.Two, settings.StopBits);
    }

    // ---- 補助 ----

    private static IStoreSettingsService NewService()
        => new StoreSettingsService(
            new StubStoreRepository(), new StubDeviceRepository(),
            NullLogger<StoreSettingsService>.Instance);

    private static StoreSettingsDraft SampleDraft() => new()
    {
        StoreName = "相模原店",
        BusinessDayStart = new TimeOnly(9, 0),
        BusinessDayEnd = new TimeOnly(22, 0),
    };

    private sealed class Fixture
    {
        private Fixture(TestDatabase db, Guid storeId)
        {
            Db = db;
            StoreId = storeId;
            Service = NewService(db);
        }

        public Guid StoreId { get; }

        public IStoreSettingsService Service { get; }

        private TestDatabase Db { get; }

        public static async Task<Fixture> CreateAsync(TestDatabase db)
        {
            db.Migrate();

            var setup = new StoreSetupService(
                new StoreRepository(db.Factory),
                new AdminCredentialRepository(db.Factory),
                new DeviceSettingsRepository(db.Factory),
                new PasswordHasher());

            var store = await setup.InitializeAsync(new StoreSetupRequest
            {
                CompanyName = "テスト株式会社",
                StoreName = "相模原店",
                BusinessDayStart = new TimeOnly(9, 0),
                BusinessDayEnd = new TimeOnly(22, 0),
                AdminPin = "12345678",
            });

            return new Fixture(db, store.Id);
        }

        public static IStoreSettingsService NewService(TestDatabase db)
            => new StoreSettingsService(
                new StoreRepository(db.Factory),
                new DeviceSettingsRepository(db.Factory),
                NullLogger<StoreSettingsService>.Instance);

        public StoreSettingsDraft Draft() => SampleDraft();

        public async Task<Guid> AddStaffAsync()
        {
            var staff = new Staff
            {
                Id = Guid.CreateVersion7(),
                StoreId = StoreId,
                Name = "山田 太郎",
                Status = StaffStatus.Active,
            };

            await new StaffRepository(Db.Factory).AddAsync(staff);

            return staff.Id;
        }
    }

    /// <summary>検証だけを見るための空実装。</summary>
    private sealed class StubStoreRepository : IStoreRepository
    {
        public Task<Store?> GetAsync(CancellationToken ct = default) => Task.FromResult<Store?>(null);

        public Task<bool> ExistsAsync(CancellationToken ct = default) => Task.FromResult(false);

        public Task AddAsync(Store store, CancellationToken ct = default) => Task.CompletedTask;

        public Task UpdateAsync(Store store, CancellationToken ct = default) => Task.CompletedTask;

        public Task<IReadOnlyList<Announcement>> GetAnnouncementsAsync(
            Guid storeId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Announcement>>([]);

        public Task ReplaceAnnouncementsAsync(
            Guid storeId, IReadOnlyList<Announcement> announcements, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class StubDeviceRepository : IDeviceSettingsRepository
    {
        public Task<DeviceSettings?> GetAsync(Guid storeId, CancellationToken ct = default)
            => Task.FromResult<DeviceSettings?>(null);

        public Task SaveAsync(DeviceSettings settings, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
