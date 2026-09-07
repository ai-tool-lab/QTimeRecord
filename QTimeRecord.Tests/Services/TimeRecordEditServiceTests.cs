using Microsoft.Extensions.Logging.Abstractions;
using QTimeRecord.Core.Data.Repositories;
using QTimeRecord.Core.Domain;
using QTimeRecord.Core.Services;
using QTimeRecord.Tests.Data;

namespace QTimeRecord.Tests.Services;

public sealed class TimeRecordEditServiceTests
{
    private static readonly DateTime Now = new(2026, 9, 6, 20, 0, 0);

    [Fact]
    public async Task 手動登録は登録方法が残る()
    {
        using var db = new TestDatabase();
        var fixture = await Fixture.CreateAsync(db);

        var record = await fixture.Service.AddAsync(fixture.Draft(TimeRecordType.ClockIn, "09:00"));

        using var separate = db.CreateSeparateContext();
        var saved = Assert.Single(separate.TimeRecords);

        // 手が入った打刻を見分けられることは要件。一覧の色分けの根拠になる。
        Assert.Equal(EntryMethod.ManualAdd, saved.EntryMethod);
        Assert.Equal(record.Id, saved.Id);
    }

    [Fact]
    public async Task 指定した営業日がそのまま保存される()
    {
        using var db = new TestDatabase();
        var fixture = await Fixture.CreateAsync(db);

        // 打刻日時は 9/7 の未明だが、営業日は 9/6 として登録する。
        // 自動判定が取りこぼした分を管理者が直せる唯一の手段（→ plan.md 11-4-4）。
        var draft = new TimeRecordDraft
        {
            StaffId = fixture.StaffId,
            WorkDate = new DateOnly(2026, 9, 6),
            RecordType = TimeRecordType.ClockOut,
            RecordedAt = new DateTime(2026, 9, 7, 2, 0, 0),
        };

        await fixture.Service.AddAsync(draft);

        using var separate = db.CreateSeparateContext();
        var saved = Assert.Single(separate.TimeRecords);

        Assert.Equal(new DateOnly(2026, 9, 6), saved.WorkDate);
        Assert.Equal(new DateTime(2026, 9, 7, 2, 0, 0), saved.RecordedAt);
    }

    [Fact]
    public async Task 営業時間外の手動登録には印が付く()
    {
        using var db = new TestDatabase();
        var fixture = await Fixture.CreateAsync(db);

        // 店舗は 09:00〜22:00。
        await fixture.Service.AddAsync(fixture.Draft(TimeRecordType.ClockIn, "05:00"));

        using var separate = db.CreateSeparateContext();
        Assert.True(Assert.Single(separate.TimeRecords).IsOutsideBusinessHours);
    }

    [Fact]
    public async Task 修正すると手修正として残る()
    {
        using var db = new TestDatabase();
        var fixture = await Fixture.CreateAsync(db);

        var original = await fixture.PunchAsync(TimeRecordType.ClockIn, "09:00");
        Assert.Equal(EntryMethod.Qr, original.EntryMethod);

        await fixture.Service.UpdateAsync(new TimeRecordDraft
        {
            RecordId = original.Id,
            StaffId = fixture.StaffId,
            WorkDate = original.WorkDate,
            RecordType = TimeRecordType.ClockIn,
            RecordedAt = fixture.At("08:45"),
            Note = "打刻機の不具合のため修正",
        });

        using var separate = db.CreateSeparateContext();
        var saved = Assert.Single(separate.TimeRecords);

        // 元が QR でも手修正にする。上書きすると本人の打刻か管理者のものか分からなくなる。
        Assert.Equal(EntryMethod.ManualEdit, saved.EntryMethod);
        Assert.Equal(fixture.At("08:45"), saved.RecordedAt);
        Assert.Equal("打刻機の不具合のため修正", saved.Note);
    }

    [Fact]
    public async Task 修正しても行は増えない()
    {
        using var db = new TestDatabase();
        var fixture = await Fixture.CreateAsync(db);

        var original = await fixture.PunchAsync(TimeRecordType.ClockIn, "09:00");

        await fixture.Service.UpdateAsync(new TimeRecordDraft
        {
            RecordId = original.Id,
            StaffId = fixture.StaffId,
            WorkDate = original.WorkDate,
            RecordType = TimeRecordType.ClockIn,
            RecordedAt = fixture.At("08:45"),
        });

        // 修正履歴は保持しない（仕様どおり）。
        Assert.Equal(1, db.CreateSeparateContext().TimeRecords.Count());
    }

    [Fact]
    public async Task 削除すると行が消える()
    {
        using var db = new TestDatabase();
        var fixture = await Fixture.CreateAsync(db);

        var record = await fixture.PunchAsync(TimeRecordType.ClockIn, "09:00");

        await fixture.Service.DeleteAsync(record.Id);

        Assert.Empty(db.CreateSeparateContext().TimeRecords);
    }

    [Fact]
    public async Task 未来の日時は弾かずに確認を求める()
    {
        using var db = new TestDatabase();
        var fixture = await Fixture.CreateAsync(db);

        var validation = await fixture.Service.ValidateAsync(
            fixture.Draft(TimeRecordType.ClockIn, "09:00", date: new DateOnly(2026, 12, 1)));

        // シフトの先行入力もありうる。弾かずに確認だけ取る。
        Assert.True(validation.IsValid);
        Assert.True(validation.HasWarning);
    }

    [Fact]
    public async Task 過去の日時は確認を求めない()
    {
        using var db = new TestDatabase();
        var fixture = await Fixture.CreateAsync(db);

        var validation = await fixture.Service.ValidateAsync(
            fixture.Draft(TimeRecordType.ClockIn, "09:00"));

        Assert.True(validation.IsValid);
        Assert.False(validation.HasWarning);
    }

    [Fact]
    public async Task 存在しないスタッフは登録できない()
    {
        using var db = new TestDatabase();
        var fixture = await Fixture.CreateAsync(db);

        var validation = await fixture.Service.ValidateAsync(new TimeRecordDraft
        {
            StaffId = Guid.CreateVersion7(),
            WorkDate = new DateOnly(2026, 9, 6),
            RecordType = TimeRecordType.ClockIn,
            RecordedAt = fixture.At("09:00"),
        });

        Assert.False(validation.IsValid);
        Assert.NotNull(validation.Error);
    }

    [Fact]
    public async Task 消えた打刻を修正しようとしたら知らせる()
    {
        using var db = new TestDatabase();
        var fixture = await Fixture.CreateAsync(db);

        var record = await fixture.PunchAsync(TimeRecordType.ClockIn, "09:00");
        await fixture.Service.DeleteAsync(record.Id);

        // 別の端末や別のタブで消された場合。黙って新規登録にしない。
        var validation = await fixture.Service.ValidateAsync(new TimeRecordDraft
        {
            RecordId = record.Id,
            StaffId = fixture.StaffId,
            WorkDate = new DateOnly(2026, 9, 6),
            RecordType = TimeRecordType.ClockIn,
            RecordedAt = fixture.At("09:00"),
        });

        Assert.False(validation.IsValid);
    }

    [Fact]
    public async Task 空白だけのメモは残さない()
    {
        using var db = new TestDatabase();
        var fixture = await Fixture.CreateAsync(db);

        var draft = fixture.Draft(TimeRecordType.ClockIn, "09:00") with { Note = "   " };

        await fixture.Service.AddAsync(draft);

        Assert.Null(Assert.Single(db.CreateSeparateContext().TimeRecords).Note);
    }

    // ---- 補助 ----

    private sealed class Fixture
    {
        private Fixture(TestDatabase db, Guid staffId)
        {
            Db = db;
            StaffId = staffId;

            Service = new TimeRecordEditService(
                new StoreRepository(db.Factory),
                new StaffRepository(db.Factory),
                new TimeRecordRepository(db.Factory),
                new TestClock(Now),
                NullLogger<TimeRecordEditService>.Instance);
        }

        public Guid StaffId { get; }

        public ITimeRecordEditService Service { get; }

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

            var staff = new Staff
            {
                Id = Guid.CreateVersion7(),
                StoreId = store.Id,
                Name = "山田 太郎",
                Status = StaffStatus.Active,
            };

            await new StaffRepository(db.Factory).AddAsync(staff);

            return new Fixture(db, staff.Id);
        }

        public DateTime At(string time, DateOnly? date = null)
            => (date ?? new DateOnly(2026, 9, 6)).ToDateTime(
                TimeOnly.Parse(time, System.Globalization.CultureInfo.InvariantCulture));

        public TimeRecordDraft Draft(TimeRecordType type, string time, DateOnly? date = null) => new()
        {
            StaffId = StaffId,
            WorkDate = date ?? new DateOnly(2026, 9, 6),
            RecordType = type,
            RecordedAt = At(time, date),
        };

        /// <summary>QR 打刻を1件入れる。修正の対象を用意するために使う。</summary>
        public async Task<TimeRecord> PunchAsync(TimeRecordType type, string time)
        {
            var record = new TimeRecord
            {
                Id = Guid.CreateVersion7(),
                StoreId = (await new StoreRepository(Db.Factory).GetAsync())!.Id,
                StaffId = StaffId,
                RecordType = type,
                RecordedAt = At(time),
                WorkDate = new DateOnly(2026, 9, 6),
                EntryMethod = EntryMethod.Qr,
            };

            await new TimeRecordRepository(Db.Factory).AddAsync(record);

            return record;
        }
    }
}
