using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using QTimeRecord.Core.Data.Repositories;
using QTimeRecord.Core.Domain;
using QTimeRecord.Core.Services;
using QTimeRecord.Tests.Data;

namespace QTimeRecord.Tests.Services;

public sealed class AttendanceQueryServiceTests
{
    [Fact]
    public async Task 該当が無ければ空を返す()
    {
        using var db = new TestDatabase();
        var fixture = await Fixture.CreateAsync(db);

        var rows = await fixture.Query.GetAsync(new AttendanceQuery { Year = 2026, Month = 9 });

        Assert.Empty(rows);
    }

    [Fact]
    public async Task 氏名と区分を添えて返す()
    {
        using var db = new TestDatabase();
        var fixture = await Fixture.CreateAsync(db);

        await fixture.PunchAsync(fixture.StaffId, new DateOnly(2026, 9, 6), TimeRecordType.ClockIn, "09:00");
        await fixture.PunchAsync(fixture.StaffId, new DateOnly(2026, 9, 6), TimeRecordType.ClockOut, "18:00");

        var rows = await fixture.Query.GetAsync(new AttendanceQuery { Year = 2026, Month = 9 });

        var row = Assert.Single(rows);
        Assert.Equal("山田 太郎", row.StaffName);
        Assert.Equal("E-0104", row.StaffNo);
        Assert.Equal("アルバイト", row.EmploymentType);
        Assert.Equal(540, row.Summary.WorkedMinutes);
    }

    [Fact]
    public async Task 営業日で月を切る()
    {
        using var db = new TestDatabase();
        var fixture = await Fixture.CreateAsync(db);

        // 営業日は 9/30 だが、打刻日時は 10/1 の未明。
        // 打刻日時で切ると、この退勤だけ10月へこぼれる。
        await fixture.AddAsync(
            fixture.StaffId, new DateOnly(2026, 9, 30), TimeRecordType.ClockIn,
            new DateTime(2026, 9, 30, 22, 0, 0));
        await fixture.AddAsync(
            fixture.StaffId, new DateOnly(2026, 9, 30), TimeRecordType.ClockOut,
            new DateTime(2026, 10, 1, 2, 0, 0));

        var september = await fixture.Query.GetAsync(new AttendanceQuery { Year = 2026, Month = 9 });
        var october = await fixture.Query.GetAsync(new AttendanceQuery { Year = 2026, Month = 10 });

        var row = Assert.Single(september);
        Assert.Equal(240, row.Summary.WorkedMinutes);
        Assert.Empty(october);
    }

    [Fact]
    public async Task 月初と月末の打刻が含まれる()
    {
        using var db = new TestDatabase();
        var fixture = await Fixture.CreateAsync(db);

        await fixture.PunchAsync(fixture.StaffId, new DateOnly(2026, 9, 1), TimeRecordType.ClockIn, "09:00");
        await fixture.PunchAsync(fixture.StaffId, new DateOnly(2026, 9, 30), TimeRecordType.ClockIn, "09:00");
        await fixture.PunchAsync(fixture.StaffId, new DateOnly(2026, 8, 31), TimeRecordType.ClockIn, "09:00");
        await fixture.PunchAsync(fixture.StaffId, new DateOnly(2026, 10, 1), TimeRecordType.ClockIn, "09:00");

        var rows = await fixture.Query.GetAsync(new AttendanceQuery { Year = 2026, Month = 9 });

        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.Equal(9, row.WorkDate.Month));
    }

    [Fact]
    public async Task スタッフで絞り込める()
    {
        using var db = new TestDatabase();
        var fixture = await Fixture.CreateAsync(db);
        var other = await fixture.AddStaffAsync("高橋 美咲");

        await fixture.PunchAsync(fixture.StaffId, new DateOnly(2026, 9, 6), TimeRecordType.ClockIn, "09:00");
        await fixture.PunchAsync(other, new DateOnly(2026, 9, 6), TimeRecordType.ClockIn, "10:00");

        var all = await fixture.Query.GetAsync(new AttendanceQuery { Year = 2026, Month = 9 });
        var one = await fixture.Query.GetAsync(
            new AttendanceQuery { Year = 2026, Month = 9, StaffId = other });

        Assert.Equal(2, all.Count);
        Assert.Equal("高橋 美咲", Assert.Single(one).StaffName);
    }

    [Fact]
    public async Task 要確認だけを絞り込める()
    {
        using var db = new TestDatabase();
        var fixture = await Fixture.CreateAsync(db);

        // 完了した日
        await fixture.PunchAsync(fixture.StaffId, new DateOnly(2026, 9, 5), TimeRecordType.ClockIn, "09:00");
        await fixture.PunchAsync(fixture.StaffId, new DateOnly(2026, 9, 5), TimeRecordType.ClockOut, "18:00");

        // 退勤を忘れた日
        await fixture.PunchAsync(fixture.StaffId, new DateOnly(2026, 9, 6), TimeRecordType.ClockIn, "09:00");

        var review = await fixture.Query.GetAsync(new AttendanceQuery
        {
            Year = 2026,
            Month = 9,
            Filter = AttendanceFilter.NeedsReview,
        });

        var row = Assert.Single(review);
        Assert.Equal(new DateOnly(2026, 9, 6), row.WorkDate);
    }

    [Fact]
    public async Task 打刻漏れだけを絞り込める()
    {
        using var db = new TestDatabase();
        var fixture = await Fixture.CreateAsync(db);

        // 異常な遷移はあるが、出勤も退勤もある日
        await fixture.PunchAsync(fixture.StaffId, new DateOnly(2026, 9, 5), TimeRecordType.ClockIn, "09:00");
        await fixture.PunchAsync(fixture.StaffId, new DateOnly(2026, 9, 5), TimeRecordType.ClockIn, "09:30");
        await fixture.PunchAsync(fixture.StaffId, new DateOnly(2026, 9, 5), TimeRecordType.ClockOut, "18:00");

        // 退勤が無い日
        await fixture.PunchAsync(fixture.StaffId, new DateOnly(2026, 9, 6), TimeRecordType.ClockIn, "09:00");

        var incomplete = await fixture.Query.GetAsync(new AttendanceQuery
        {
            Year = 2026,
            Month = 9,
            Filter = AttendanceFilter.Incomplete,
        });

        // どちらも「要確認」だが、打刻漏れは 9/6 だけ。
        Assert.Equal(new DateOnly(2026, 9, 6), Assert.Single(incomplete).WorkDate);
    }

    [Fact]
    public async Task 退職したスタッフの過去の勤怠も残る()
    {
        using var db = new TestDatabase();
        var fixture = await Fixture.CreateAsync(db);

        await fixture.PunchAsync(fixture.StaffId, new DateOnly(2026, 9, 6), TimeRecordType.ClockIn, "09:00");
        await fixture.SetStatusAsync(fixture.StaffId, StaffStatus.Retired);

        var rows = await fixture.Query.GetAsync(new AttendanceQuery { Year = 2026, Month = 9 });

        // 在籍中だけを引くと、退職者の過去の勤怠が氏名なしで並ぶ。
        Assert.Equal("山田 太郎", Assert.Single(rows).StaffName);
    }

    [Fact]
    public async Task 既定は新しい営業日が上に来る()
    {
        using var db = new TestDatabase();
        var fixture = await Fixture.CreateAsync(db);

        foreach (var day in new[] { 4, 6, 5 })
        {
            await fixture.PunchAsync(
                fixture.StaffId, new DateOnly(2026, 9, day), TimeRecordType.ClockIn, "09:00");
        }

        var rows = await fixture.Query.GetAsync(new AttendanceQuery { Year = 2026, Month = 9 });

        // 指定しなければ降順。管理者が最初に見たいのは直近の勤怠。
        Assert.Equal(
            new[] { 6, 5, 4 }.Select(d => new DateOnly(2026, 9, d)),
            rows.Select(r => r.WorkDate));
    }

    [Fact]
    public async Task 昇順を指定すると古い営業日が上に来る()
    {
        using var db = new TestDatabase();
        var fixture = await Fixture.CreateAsync(db);

        foreach (var day in new[] { 4, 6, 5 })
        {
            await fixture.PunchAsync(
                fixture.StaffId, new DateOnly(2026, 9, day), TimeRecordType.ClockIn, "09:00");
        }

        var rows = await fixture.Query.GetAsync(new AttendanceQuery
        {
            Year = 2026,
            Month = 9,
            Sort = AttendanceSort.DateAscending,
        });

        Assert.Equal(
            new[] { 4, 5, 6 }.Select(d => new DateOnly(2026, 9, d)),
            rows.Select(r => r.WorkDate));
    }

    [Fact]
    public async Task 同じ営業日のなかも並び順に従う()
    {
        using var db = new TestDatabase();
        var fixture = await Fixture.CreateAsync(db);
        var other = await fixture.AddStaffAsync("高橋 美咲");

        var date = new DateOnly(2026, 9, 6);

        await fixture.PunchAsync(fixture.StaffId, date, TimeRecordType.ClockIn, "09:00");
        await fixture.PunchAsync(other, date, TimeRecordType.ClockIn, "13:00");

        var descending = await fixture.Query.GetAsync(new AttendanceQuery { Year = 2026, Month = 9 });
        var ascending = await fixture.Query.GetAsync(new AttendanceQuery
        {
            Year = 2026,
            Month = 9,
            Sort = AttendanceSort.DateAscending,
        });

        // 日付だけで並べると、同じ日の行の順序が取得方法に左右されて安定しない。
        Assert.Equal("高橋 美咲", descending[0].StaffName);
        Assert.Equal("山田 太郎", ascending[0].StaffName);
    }

    [Fact]
    public async Task 打刻を修正すると集計が計算し直される()
    {
        using var db = new TestDatabase();
        var fixture = await Fixture.CreateAsync(db);

        var date = new DateOnly(2026, 9, 6);

        await fixture.PunchAsync(fixture.StaffId, date, TimeRecordType.ClockIn, "09:00");
        await fixture.PunchAsync(fixture.StaffId, date, TimeRecordType.ClockOut, "18:00");

        var before = Assert.Single(await fixture.Query.GetAsync(new AttendanceQuery
        {
            Year = 2026,
            Month = 9,
        }));

        Assert.Equal(540, before.Summary.WorkedMinutes);

        // 出勤を 08:30 に直す。
        var clockIn = before.Summary.Records.First(r => r.RecordType == TimeRecordType.ClockIn);

        await fixture.Editor.UpdateAsync(new TimeRecordDraft
        {
            RecordId = clockIn.Id,
            StaffId = fixture.StaffId,
            WorkDate = date,
            RecordType = TimeRecordType.ClockIn,
            RecordedAt = date.ToDateTime(new TimeOnly(8, 30)),
            Note = "打刻漏れのため修正",
        });

        var after = Assert.Single(await fixture.Query.GetAsync(new AttendanceQuery
        {
            Year = 2026,
            Month = 9,
        }));

        // 集計を保存していたら、ここが 540 のまま古い値になる。
        Assert.Equal(570, after.Summary.WorkedMinutes);
        Assert.Equal("打刻漏れのため修正", after.Summary.Note);
        Assert.True(after.Summary.HasManualEdit);
    }

    [Fact]
    public async Task 打刻を削除すると集計から消える()
    {
        using var db = new TestDatabase();
        var fixture = await Fixture.CreateAsync(db);

        var date = new DateOnly(2026, 9, 6);

        await fixture.PunchAsync(fixture.StaffId, date, TimeRecordType.ClockIn, "09:00");
        await fixture.PunchAsync(fixture.StaffId, date, TimeRecordType.ClockOut, "18:00");

        var row = Assert.Single(await fixture.Query.GetAsync(new AttendanceQuery
        {
            Year = 2026,
            Month = 9,
        }));

        var clockOut = row.Summary.Records.First(r => r.RecordType == TimeRecordType.ClockOut);

        await fixture.Editor.DeleteAsync(clockOut.Id);

        var after = Assert.Single(await fixture.Query.GetAsync(new AttendanceQuery
        {
            Year = 2026,
            Month = 9,
        }));

        // 誤登録を取り消したら、その日は「退勤なし」として要確認に戻る。
        Assert.Null(after.Summary.ClockOutAt);
        Assert.Equal(AttendanceStatus.NeedsReview, after.Status);
    }

    [Fact]
    public async Task スタッフ二十名一か月ぶんを三百ミリ秒以内に返す()
    {
        using var db = new TestDatabase();
        var fixture = await Fixture.CreateAsync(db);

        // 20名 × 30日 × 出勤/中抜け開始/中抜け終了/退勤 = 2,400 件。
        for (var i = 0; i < 20; i++)
        {
            var staffId = i == 0 ? fixture.StaffId : await fixture.AddStaffAsync($"スタッフ{i}");

            for (var day = 1; day <= 30; day++)
            {
                var date = new DateOnly(2026, 9, day);

                await fixture.PunchAsync(staffId, date, TimeRecordType.ClockIn, "09:00");
                await fixture.PunchAsync(staffId, date, TimeRecordType.BreakStart, "12:00");
                await fixture.PunchAsync(staffId, date, TimeRecordType.BreakEnd, "13:00");
                await fixture.PunchAsync(staffId, date, TimeRecordType.ClockOut, "18:00");
            }
        }

        // 1回目は EF のクエリ組み立てが入る。実際の操作感は2回目以降に近い。
        await fixture.Query.GetAsync(new AttendanceQuery { Year = 2026, Month = 9 });

        var stopwatch = Stopwatch.StartNew();
        var rows = await fixture.Query.GetAsync(new AttendanceQuery { Year = 2026, Month = 9 });
        stopwatch.Stop();

        Assert.Equal(600, rows.Count);
        Assert.True(
            stopwatch.ElapsedMilliseconds < 300,
            $"一覧の取得に {stopwatch.ElapsedMilliseconds}ms かかった（上限 300ms）。");
    }

    // ---- 補助 ----

    private sealed class Fixture
    {
        private Fixture(TestDatabase db, Guid storeId, Guid staffId)
        {
            Db = db;
            StoreId = storeId;
            StaffId = staffId;

            Query = new AttendanceQueryService(
                new StoreRepository(db.Factory), new TimeRecordRepository(db.Factory));
        }

        public Guid StaffId { get; }

        public IAttendanceQueryService Query { get; }

        /// <summary>手動修正・削除。集計が計算し直されることの確認に使う。</summary>
        public ITimeRecordEditService Editor => new TimeRecordEditService(
            new StoreRepository(Db.Factory),
            new StaffRepository(Db.Factory),
            new TimeRecordRepository(Db.Factory),
            new TestClock(new DateTime(2026, 12, 31, 0, 0, 0)),
            NullLogger<TimeRecordEditService>.Instance);

        private TestDatabase Db { get; }

        private Guid StoreId { get; }

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
                StaffNo = "E-0104",
                Name = "山田 太郎",
                EmploymentType = "アルバイト",
                Status = StaffStatus.Active,
            };

            await new StaffRepository(db.Factory).AddAsync(staff);

            return new Fixture(db, store.Id, staff.Id);
        }

        public async Task<Guid> AddStaffAsync(string name)
        {
            var staff = new Staff
            {
                Id = Guid.CreateVersion7(),
                StoreId = StoreId,
                Name = name,
                Status = StaffStatus.Active,
            };

            await new StaffRepository(Db.Factory).AddAsync(staff);

            return staff.Id;
        }

        public async Task SetStatusAsync(Guid staffId, StaffStatus status)
        {
            var repository = new StaffRepository(Db.Factory);
            var staff = await repository.GetByIdAsync(staffId);

            staff!.Status = status;

            await repository.UpdateAsync(staff);
        }

        public Task PunchAsync(Guid staffId, DateOnly workDate, TimeRecordType type, string time)
            => AddAsync(
                staffId,
                workDate,
                type,
                workDate.ToDateTime(
                    TimeOnly.Parse(time, System.Globalization.CultureInfo.InvariantCulture)));

        public Task AddAsync(Guid staffId, DateOnly workDate, TimeRecordType type, DateTime recordedAt)
        {
            var service = new TimeRecordEditService(
                new StoreRepository(Db.Factory),
                new StaffRepository(Db.Factory),
                new TimeRecordRepository(Db.Factory),
                new TestClock(new DateTime(2026, 12, 31, 0, 0, 0)),
                NullLogger<TimeRecordEditService>.Instance);

            return service.AddAsync(new TimeRecordDraft
            {
                StaffId = staffId,
                WorkDate = workDate,
                RecordType = type,
                RecordedAt = recordedAt,
            });
        }
    }
}
