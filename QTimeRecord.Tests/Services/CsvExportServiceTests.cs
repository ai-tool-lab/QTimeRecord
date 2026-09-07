using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using QTimeRecord.Core.Data.Repositories;
using QTimeRecord.Core.Domain;
using QTimeRecord.Core.Services;
using QTimeRecord.Tests.Data;

namespace QTimeRecord.Tests.Services;

public sealed class CsvExportServiceTests
{
    [Fact]
    public async Task 対象が無くてもヘッダーは出す()
    {
        using var db = new TestDatabase();
        var fixture = await Fixture.CreateAsync(db);

        var lines = await fixture.ExportLinesAsync();

        // 空ファイルだと、出力に失敗したのか対象が無かったのか区別できない。
        var header = Assert.Single(lines);
        Assert.StartsWith("店舗ID,店舗名,スタッフID,社員番号,スタッフ名,区分,日付,曜日",
            header, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 列は仕様どおり十七列()
    {
        Assert.Equal(17, CsvExportService.Headers.Length);

        using var db = new TestDatabase();
        var fixture = await Fixture.CreateAsync(db);

        await fixture.PunchAsync("山田 太郎", new DateOnly(2026, 9, 7), TimeRecordType.ClockIn, "09:00");

        var lines = await fixture.ExportLinesAsync();

        Assert.Equal(17, lines[1].Split(',').Length);
    }

    [Fact]
    public async Task 打刻の内容が列に並ぶ()
    {
        using var db = new TestDatabase();
        var fixture = await Fixture.CreateAsync(db);

        var date = new DateOnly(2026, 9, 7);

        await fixture.PunchAsync("山田 太郎", date, TimeRecordType.ClockIn, "09:00");
        await fixture.PunchAsync("山田 太郎", date, TimeRecordType.BreakStart, "12:00");
        await fixture.PunchAsync("山田 太郎", date, TimeRecordType.BreakEnd, "13:00");
        await fixture.PunchAsync("山田 太郎", date, TimeRecordType.ClockOut, "18:30");

        var fields = (await fixture.ExportLinesAsync())[1].Split(',');

        Assert.Equal("相模原店", fields[1]);
        Assert.Equal("E-0104", fields[3]);
        Assert.Equal("山田 太郎", fields[4]);
        Assert.Equal("アルバイト", fields[5]);
        Assert.Equal("2026-09-07", fields[6]);
        Assert.Equal("月", fields[7]);
        Assert.Equal("09:00", fields[8]);
        Assert.Equal("18:30", fields[9]);
        Assert.Equal("12:00", fields[10]);
        Assert.Equal("13:00", fields[11]);
        Assert.Equal("1", fields[12]);
        Assert.Equal("60", fields[13]);

        // 9:00-18:30 の 570分 から中抜け60分を引いた 510分。
        Assert.Equal("510", fields[14]);
        Assert.Equal("通常", fields[15]);
    }

    [Fact]
    public async Task 未打刻の欄は空欄にする()
    {
        using var db = new TestDatabase();
        var fixture = await Fixture.CreateAsync(db);

        await fixture.PunchAsync("山田 太郎", new DateOnly(2026, 9, 7), TimeRecordType.ClockIn, "09:00");

        var fields = (await fixture.ExportLinesAsync())[1].Split(',');

        // 「00:00」にすると0時の打刻と区別できない。
        Assert.Equal("09:00", fields[8]);
        Assert.Equal(string.Empty, fields[9]);
        Assert.Equal(string.Empty, fields[10]);
        Assert.Equal("0", fields[12]);
        Assert.Equal("0", fields[13]);

        // 退勤が無ければ実労働は出せない。
        Assert.Equal(string.Empty, fields[14]);
    }

    [Fact]
    public async Task 氏名にカンマがあっても列がずれない()
    {
        using var db = new TestDatabase();
        var fixture = await Fixture.CreateAsync(db);

        await fixture.PunchAsync("山田, 太郎", new DateOnly(2026, 9, 7), TimeRecordType.ClockIn, "09:00");

        var line = (await fixture.ExportLinesAsync())[1];

        Assert.Contains("\"山田, 太郎\"", line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task メモに引用符や改行があっても壊れない()
    {
        using var db = new TestDatabase();
        var fixture = await Fixture.CreateAsync(db);

        var date = new DateOnly(2026, 9, 7);
        await fixture.PunchAsync("山田 太郎", date, TimeRecordType.ClockIn, "09:00");
        await fixture.PunchAsync(
            "山田 太郎", date, TimeRecordType.ClockOut, "18:00",
            EntryMethod.ManualEdit, "打刻機が\"不調\"のため\r\n代理で登録");

        var content = await fixture.ExportTextAsync();

        // 囲まないと1件の値が2行に割れ、列がずれる。読み直して確かめる。
        var records = CsvReader.Parse(content);

        Assert.Equal(2, records.Count);
        Assert.Equal(17, records[1].Count);
        Assert.Equal("打刻機が\"不調\"のため\r\n代理で登録", records[1][16]);
    }

    [Fact]
    public async Task 手入力と手修正を見分けられる()
    {
        using var db = new TestDatabase();
        var fixture = await Fixture.CreateAsync(db);

        await fixture.PunchAsync(
            "山田 太郎", new DateOnly(2026, 9, 7), TimeRecordType.ClockIn, "09:00",
            EntryMethod.ManualAdd);
        await fixture.PunchAsync(
            "高橋 美咲", new DateOnly(2026, 9, 7), TimeRecordType.ClockIn, "10:00",
            EntryMethod.ManualEdit);

        var lines = await fixture.ExportLinesAsync();

        Assert.Contains(lines, l => l.Split(',')[15] == "手入力");
        Assert.Contains(lines, l => l.Split(',')[15] == "手修正");
    }

    [Fact]
    public async Task 画面の絞り込みと同じ範囲を出す()
    {
        using var db = new TestDatabase();
        var fixture = await Fixture.CreateAsync(db);

        var date = new DateOnly(2026, 9, 7);

        // 完了した日と、退勤を忘れた日。
        await fixture.PunchAsync("山田 太郎", date, TimeRecordType.ClockIn, "09:00");
        await fixture.PunchAsync("山田 太郎", date, TimeRecordType.ClockOut, "18:00");
        await fixture.PunchAsync("高橋 美咲", date, TimeRecordType.ClockIn, "10:00");

        var all = await fixture.ExportLinesAsync();
        var review = await fixture.ExportLinesAsync(AttendanceFilter.NeedsReview);

        // 別の条件で出すと、画面で確認した内容と給与へ渡す内容が食い違う。
        Assert.Equal(3, all.Count);
        Assert.Equal(2, review.Count);
        Assert.Contains("高橋 美咲", review[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task 別の月の打刻は含めない()
    {
        using var db = new TestDatabase();
        var fixture = await Fixture.CreateAsync(db);

        await fixture.PunchAsync("山田 太郎", new DateOnly(2026, 9, 7), TimeRecordType.ClockIn, "09:00");
        await fixture.PunchAsync("山田 太郎", new DateOnly(2026, 8, 31), TimeRecordType.ClockIn, "09:00");

        var lines = await fixture.ExportLinesAsync();

        Assert.Equal(2, lines.Count);
        Assert.Contains("2026-09-07", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task 先頭にBOMを付ける()
    {
        using var db = new TestDatabase();
        var fixture = await Fixture.CreateAsync(db);

        var bytes = await fixture.Service.ExportAsync(fixture.Query());

        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
    }

    [Theory]
    [InlineData("101", "QTimeRecord_勤務状況_101_202609.csv")]
    [InlineData(null, "QTimeRecord_勤務状況_202609.csv")]
    [InlineData("  ", "QTimeRecord_勤務状況_202609.csv")]
    public void ファイル名は店舗コードと年月から作る(string? storeCode, string expected)
    {
        // 店舗コードが未設定のとき、空の区切りを残さない。
        Assert.Equal(expected, CsvExportService.BuildFileName(storeCode, 2026, 9));
    }

    [Fact]
    public async Task ファイル名に店舗コードが入る()
    {
        using var db = new TestDatabase();
        var fixture = await Fixture.CreateAsync(db);

        Assert.Equal(
            "QTimeRecord_勤務状況_101_202609.csv",
            await fixture.Service.SuggestFileNameAsync(fixture.Query()));
    }

    // ---- 補助 ----

    private sealed class Fixture
    {
        private readonly Dictionary<string, Guid> _staff = [];

        private Fixture(TestDatabase db, Guid storeId)
        {
            Db = db;
            StoreId = storeId;

            Service = new CsvExportService(
                new StoreRepository(db.Factory),
                new AttendanceQueryService(
                    new StoreRepository(db.Factory), new TimeRecordRepository(db.Factory)),
                NullLogger<CsvExportService>.Instance);
        }

        public ICsvExportService Service { get; }

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
                StoreCode = "101",
                BusinessDayStart = new TimeOnly(9, 0),
                BusinessDayEnd = new TimeOnly(22, 0),
                AdminPin = "12345678",
            });

            return new Fixture(db, store.Id);
        }

        public AttendanceQuery Query(AttendanceFilter filter = AttendanceFilter.All)
            => new() { Year = 2026, Month = 9, Filter = filter };

        public async Task<string> ExportTextAsync(AttendanceFilter filter = AttendanceFilter.All)
        {
            var bytes = await Service.ExportAsync(Query(filter));

            return Encoding.UTF8.GetString(bytes[3..]);
        }

        public async Task<IReadOnlyList<string>> ExportLinesAsync(
            AttendanceFilter filter = AttendanceFilter.All)
        {
            var text = await ExportTextAsync(filter);

            return text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        }

        public async Task PunchAsync(
            string name,
            DateOnly workDate,
            TimeRecordType type,
            string time,
            EntryMethod method = EntryMethod.Qr,
            string? note = null)
        {
            if (!_staff.TryGetValue(name, out var staffId))
            {
                var staff = new Staff
                {
                    Id = Guid.CreateVersion7(),
                    StoreId = StoreId,
                    StaffNo = _staff.Count == 0 ? "E-0104" : $"E-{_staff.Count:D4}",
                    Name = name,
                    EmploymentType = "アルバイト",
                    Status = StaffStatus.Active,
                };

                await new StaffRepository(Db.Factory).AddAsync(staff);

                staffId = staff.Id;
                _staff[name] = staffId;
            }

            await new TimeRecordRepository(Db.Factory).AddAsync(new TimeRecord
            {
                Id = Guid.CreateVersion7(),
                StoreId = StoreId,
                StaffId = staffId,
                RecordType = type,
                RecordedAt = workDate.ToDateTime(
                    TimeOnly.Parse(time, System.Globalization.CultureInfo.InvariantCulture)),
                WorkDate = workDate,
                EntryMethod = method,
                Note = note,
            });
        }
    }
}
