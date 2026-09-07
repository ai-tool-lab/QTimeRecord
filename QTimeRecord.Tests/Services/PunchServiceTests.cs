using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using QTimeRecord.Core.Data.Repositories;
using QTimeRecord.Core.Domain;
using QTimeRecord.Core.Services;
using QTimeRecord.Tests.Data;

namespace QTimeRecord.Tests.Services;

public sealed class PunchServiceTests
{
    /// <summary>11:00 開店・翌 05:00 閉店。営業日が日をまたぐ店舗で確かめる。</summary>
    private static readonly TimeOnly OpenAt = new(11, 0);
    private static readonly TimeOnly CloseAt = new(5, 0);

    [Fact]
    public async Task 出勤を記録すると営業日が付いてDBに残る()
    {
        using var db = new TestDatabase();
        var fixture = await Fixture.CreateAsync(db, new DateTime(2026, 9, 6, 18, 0, 0));

        var outcome = await fixture.Service.PunchAsync(fixture.Request(TimeRecordType.ClockIn));

        Assert.Equal(PunchStatus.Recorded, outcome.Status);
        Assert.Equal("出勤を記録しました", outcome.Message);

        // 別のコンテキストから読み直す。追跡中の値ではなく、実際に書けたかを見る。
        using var separate = db.CreateSeparateContext();
        var saved = Assert.Single(separate.TimeRecords);

        Assert.Equal(TimeRecordType.ClockIn, saved.RecordType);
        Assert.Equal(new DateOnly(2026, 9, 6), saved.WorkDate);
        Assert.Equal(EntryMethod.Qr, saved.EntryMethod);
        Assert.False(saved.IsOutsideBusinessHours);
    }

    [Fact]
    public async Task 日をまたいだ退勤は出勤と同じ営業日に入る()
    {
        using var db = new TestDatabase();
        var fixture = await Fixture.CreateAsync(db, new DateTime(2026, 9, 6, 22, 0, 0));

        await fixture.Service.PunchAsync(fixture.Request(TimeRecordType.ClockIn));

        // 翌 02:00。カレンダー日付は 09/07 だが、営業日はまだ 09/06。
        fixture.Clock.Advance(TimeSpan.FromHours(4));
        var outcome = await fixture.Service.PunchAsync(fixture.Request(TimeRecordType.ClockOut));

        Assert.Equal(PunchStatus.Recorded, outcome.Status);
        Assert.NotNull(outcome.Record);
        Assert.Equal(new DateOnly(2026, 9, 7), DateOnly.FromDateTime(outcome.Record.RecordedAt));
        Assert.Equal(new DateOnly(2026, 9, 6), outcome.Record.WorkDate);
    }

    [Fact]
    public async Task 営業時間外の打刻は時間外として記録される()
    {
        using var db = new TestDatabase();

        // 08:00。11:00 開店・翌 05:00 閉店の店舗では閉店中にあたる。
        var fixture = await Fixture.CreateAsync(db, new DateTime(2026, 9, 6, 8, 0, 0));

        var outcome = await fixture.Service.PunchAsync(fixture.Request(TimeRecordType.ClockIn));

        Assert.Equal(PunchStatus.Recorded, outcome.Status);
        Assert.NotNull(outcome.Record);
        Assert.True(outcome.Record.IsOutsideBusinessHours);
    }

    [Fact]
    public async Task 異常な遷移は確認を求めてから記録する()
    {
        using var db = new TestDatabase();
        var fixture = await Fixture.CreateAsync(db, new DateTime(2026, 9, 6, 18, 0, 0));

        await fixture.Service.PunchAsync(fixture.Request(TimeRecordType.ClockIn));
        fixture.Clock.Advance(TimeSpan.FromMinutes(5));

        // 出勤 → 出勤。確認を取るまでは書かない。
        var asked = await fixture.Service.PunchAsync(fixture.Request(TimeRecordType.ClockIn));

        Assert.Equal(PunchStatus.NeedsConfirmation, asked.Status);
        Assert.Null(asked.Record);
        Assert.Equal(1, db.CreateSeparateContext().TimeRecords.Count());

        var confirmed = await fixture.Service.PunchAsync(
            fixture.Request(TimeRecordType.ClockIn, warningConfirmed: true));

        Assert.Equal(PunchStatus.Recorded, confirmed.Status);
        Assert.Equal(2, db.CreateSeparateContext().TimeRecords.Count());
    }

    [Fact]
    public async Task 確認済みでも重複は記録しない()
    {
        using var db = new TestDatabase();
        var fixture = await Fixture.CreateAsync(db, new DateTime(2026, 9, 6, 18, 0, 0));

        await fixture.Service.PunchAsync(fixture.Request(TimeRecordType.ClockIn));

        fixture.Clock.Advance(TimeSpan.FromSeconds(10));
        var outcome = await fixture.Service.PunchAsync(
            fixture.Request(TimeRecordType.ClockIn, warningConfirmed: true));

        // 連打の抑止は確認では覆せない。覆せると二度読みがそのまま通る。
        Assert.Equal(PunchStatus.Duplicate, outcome.Status);
        Assert.Equal(1, db.CreateSeparateContext().TimeRecords.Count());
    }

    [Fact]
    public async Task 保存に失敗したら成功として返さない()
    {
        using var db = new TestDatabase();
        var fixture = await Fixture.CreateAsync(db, new DateTime(2026, 9, 6, 18, 0, 0));

        var failing = new FailingTimeRecordRepository(fixture.Records);
        var service = fixture.WithRecords(failing);

        var outcome = await service.PunchAsync(fixture.Request(TimeRecordType.ClockIn));

        // 打刻が消えたことに気づけないのが最悪の壊れ方。必ず失敗として返す。
        Assert.Equal(PunchStatus.Failed, outcome.Status);
        Assert.Null(outcome.Record);
        Assert.Empty(db.CreateSeparateContext().TimeRecords);
    }

    [Theory]
    [InlineData(StaffStatus.OnLeave)]
    [InlineData(StaffStatus.Retired)]
    public async Task 在籍中でないスタッフは打刻できない(StaffStatus status)
    {
        using var db = new TestDatabase();
        var fixture = await Fixture.CreateAsync(db, new DateTime(2026, 9, 6, 18, 0, 0), status);

        var outcome = await fixture.Service.PunchAsync(fixture.Request(TimeRecordType.ClockIn));

        Assert.Equal(PunchStatus.Rejected, outcome.Status);
        Assert.Empty(db.CreateSeparateContext().TimeRecords);
    }

    [Fact]
    public async Task 現況には状態と直近の打刻が入る()
    {
        using var db = new TestDatabase();
        var fixture = await Fixture.CreateAsync(db, new DateTime(2026, 9, 6, 18, 0, 0));

        var before = await fixture.Service.GetContextAsync(fixture.StaffId);
        Assert.NotNull(before);
        Assert.Equal(PunchState.NotClockedIn, before.State);
        Assert.Null(before.LatestRecord);

        await fixture.Service.PunchAsync(fixture.Request(TimeRecordType.ClockIn));
        fixture.Clock.Advance(TimeSpan.FromHours(1));

        var after = await fixture.Service.GetContextAsync(fixture.StaffId);
        Assert.NotNull(after);
        Assert.Equal(PunchState.Working, after.State);
        Assert.NotNull(after.LatestRecord);
        Assert.Equal(TimeRecordType.ClockIn, after.LatestRecord.RecordType);
        Assert.Equal(new DateOnly(2026, 9, 6), after.WorkDate);
    }

    [Fact]
    public async Task 前営業日の打刻は当日の状態に持ち越さない()
    {
        using var db = new TestDatabase();
        var fixture = await Fixture.CreateAsync(db, new DateTime(2026, 9, 6, 18, 0, 0));

        await fixture.Service.PunchAsync(fixture.Request(TimeRecordType.ClockIn));
        await fixture.Service.PunchAsync(
            fixture.Request(TimeRecordType.ClockOut, warningConfirmed: true));

        // 翌日の営業時間内。前日の「退勤済」を引き継ぐと、出勤が毎回警告になる。
        fixture.Clock.Advance(TimeSpan.FromDays(1));

        var context = await fixture.Service.GetContextAsync(fixture.StaffId);

        Assert.NotNull(context);
        Assert.Equal(PunchState.NotClockedIn, context.State);
        Assert.Equal(new DateOnly(2026, 9, 7), context.WorkDate);

        var outcome = await fixture.Service.PunchAsync(fixture.Request(TimeRecordType.ClockIn));
        Assert.Equal(PunchStatus.Recorded, outcome.Status);
    }

    // ---- 補助 ----

    private sealed class Fixture
    {
        private Fixture(
            TestDatabase db, TestClock clock, Guid staffId, ITimeRecordRepository records)
        {
            Db = db;
            Clock = clock;
            StaffId = staffId;
            Records = records;
            Service = WithRecords(records);
        }

        public TestClock Clock { get; }

        public Guid StaffId { get; }

        public ITimeRecordRepository Records { get; }

        public IPunchService Service { get; }

        private TestDatabase Db { get; }

        public static async Task<Fixture> CreateAsync(
            TestDatabase db, DateTime now, StaffStatus status = StaffStatus.Active)
        {
            db.Migrate();

            var stores = new StoreRepository(db.Factory);
            var staff = new StaffRepository(db.Factory);

            var store = new Store
            {
                Id = Guid.CreateVersion7(),
                CompanyName = "テスト株式会社",
                StoreName = "相模原店",
                BusinessDayStart = OpenAt,
                BusinessDayEnd = CloseAt,
            };

            await stores.AddAsync(store);

            var member = new Staff
            {
                Id = Guid.CreateVersion7(),
                StoreId = store.Id,
                Name = "山田 太郎",
                Status = status,
            };

            await staff.AddAsync(member);

            return new Fixture(db, new TestClock(now), member.Id, new TimeRecordRepository(db.Factory));
        }

        public IPunchService WithRecords(ITimeRecordRepository records) => new PunchService(
            new StoreRepository(Db.Factory),
            new StaffRepository(Db.Factory),
            records,
            Clock,
            NullLogger<PunchService>.Instance);

        public PunchRequest Request(TimeRecordType type, bool warningConfirmed = false) => new()
        {
            StaffId = StaffId,
            RecordType = type,
            WarningConfirmed = warningConfirmed,
        };
    }

    /// <summary>書き込みだけ失敗する Repository。ディスク障害・ロックの代わり。</summary>
    private sealed class FailingTimeRecordRepository(ITimeRecordRepository inner) : ITimeRecordRepository
    {
        public Task AddAsync(TimeRecord record, CancellationToken ct = default)
            => throw new IOException("書き込みに失敗しました。");

        public Task<TimeRecord?> GetByIdAsync(Guid recordId, CancellationToken ct = default)
            => inner.GetByIdAsync(recordId, ct);

        public Task UpdateAsync(TimeRecord record, CancellationToken ct = default)
            => throw new IOException("書き込みに失敗しました。");

        public Task DeleteAsync(Guid recordId, CancellationToken ct = default)
            => inner.DeleteAsync(recordId, ct);

        public Task<IReadOnlyList<TimeRecord>> ListByMonthAsync(
            Guid storeId, int year, int month, Guid? staffId = null, CancellationToken ct = default)
            => inner.ListByMonthAsync(storeId, year, month, staffId, ct);

        public Task<IReadOnlyList<TimeRecord>> ListByWorkDateAsync(
            Guid storeId, Guid staffId, DateOnly workDate, CancellationToken ct = default)
            => inner.ListByWorkDateAsync(storeId, staffId, workDate, ct);

        public Task<TimeRecord?> GetLatestAsync(Guid staffId, CancellationToken ct = default)
            => inner.GetLatestAsync(staffId, ct);

        public Task<TimeRecord?> GetOpenClockInAsync(Guid staffId, CancellationToken ct = default)
            => inner.GetOpenClockInAsync(staffId, ct);
    }
}
