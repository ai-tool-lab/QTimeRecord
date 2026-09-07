using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QTimeRecord.Core.Domain;

namespace QTimeRecord.Tests.Data;

/// <summary>
/// 空の DB ファイルからマイグレーションでスキーマが構築されることを確認する。
/// </summary>
public sealed class MigrationTests
{
    private static readonly string[] ExpectedTables =
    [
        "stores",
        "announcements",
        "staff",
        "staff_qr_tokens",
        "time_records",
        "device_settings",
        "admin_credentials",
    ];

    private static readonly string[] ExpectedIndexes =
    [
        "ix_announcements_store_id_display_order",
        "ix_staff_store_id_status",
        "ix_staff_store_id_staff_no",
        "ix_staff_qr_tokens_token",
        "ix_staff_qr_tokens_staff_id_revoked_at",
        "ix_time_records_store_id_work_date",
        "ix_time_records_staff_id_work_date",
        "ix_time_records_staff_id_recorded_at",
    ];

    [Fact]
    public void 空のDBからすべてのテーブルが作られる()
    {
        using var db = new TestDatabase();

        db.Migrate();

        var tables = QueryNames(db, "table");

        foreach (var expected in ExpectedTables)
        {
            Assert.Contains(expected, tables);
        }
    }

    [Fact]
    public void 想定した索引がすべて作られる()
    {
        using var db = new TestDatabase();

        db.Migrate();

        var indexes = QueryNames(db, "index");

        foreach (var expected in ExpectedIndexes)
        {
            Assert.Contains(expected, indexes);
        }
    }

    [Fact]
    public void 外部キーが有効になっている()
    {
        using var db = new TestDatabase();
        db.Migrate();

        // PRAGMA は接続ごとの設定。インターセプタが効いていないと 0 のままになる。
        Assert.Equal(1L, ExecuteScalar(db, "PRAGMA foreign_keys;"));
    }

    [Fact]
    public void WALと同期設定が適用されている()
    {
        using var db = new TestDatabase();
        db.Migrate();

        Assert.Equal("wal", ExecuteScalar(db, "PRAGMA journal_mode;")?.ToString());

        // synchronous=FULL は 2。電源断で直前の打刻を失わないために必要。
        Assert.Equal(2L, ExecuteScalar(db, "PRAGMA synchronous;"));
    }

    [Fact]
    public void 打刻のあるスタッフは削除できない()
    {
        using var db = new TestDatabase();
        db.Migrate();

        var (storeId, staffId) = SeedStoreAndStaff(db);

        db.Context.TimeRecords.Add(new TimeRecord
        {
            Id = Guid.CreateVersion7(),
            StoreId = storeId,
            StaffId = staffId,
            RecordType = TimeRecordType.ClockIn,
            RecordedAt = new DateTime(2026, 9, 5, 11, 0, 0),
            WorkDate = new DateOnly(2026, 9, 5),
            EntryMethod = EntryMethod.Qr,
        });
        db.Context.SaveChanges();

        // 退職は Status で表す運用だが、誤った物理削除は DB 側で止まること。
        // EF の変更追跡を経由しない生 SQL で、外部キー制約そのものを確認する。
        var ex = Assert.Throws<SqliteException>(
            () => db.Context.Database.ExecuteSqlRaw("DELETE FROM staff WHERE id = {0};", staffId));

        Assert.Contains("FOREIGN KEY constraint failed", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void 列挙型が大文字スネークケースで保存される()
    {
        using var db = new TestDatabase();
        db.Migrate();

        var (storeId, staffId) = SeedStoreAndStaff(db);

        db.Context.TimeRecords.Add(new TimeRecord
        {
            Id = Guid.CreateVersion7(),
            StoreId = storeId,
            StaffId = staffId,
            RecordType = TimeRecordType.BreakStart,
            RecordedAt = DateTime.Now,
            WorkDate = new DateOnly(2026, 9, 5),
            EntryMethod = EntryMethod.ManualAdd,
        });
        db.Context.SaveChanges();

        Assert.Equal("BREAK_START", ExecuteScalar(db, "SELECT record_type FROM time_records;")?.ToString());
        Assert.Equal("MANUAL_ADD", ExecuteScalar(db, "SELECT entry_method FROM time_records;")?.ToString());
        Assert.Equal("ACTIVE", ExecuteScalar(db, "SELECT status FROM staff;")?.ToString());
    }

    [Fact]
    public void 営業日と打刻日時が別々に保存される()
    {
        using var db = new TestDatabase();
        db.Migrate();

        var (storeId, staffId) = SeedStoreAndStaff(db);

        // 22:00 出勤の勤務が翌 02:00 に退勤したケース。
        db.Context.TimeRecords.Add(new TimeRecord
        {
            Id = Guid.CreateVersion7(),
            StoreId = storeId,
            StaffId = staffId,
            RecordType = TimeRecordType.ClockOut,
            RecordedAt = new DateTime(2026, 9, 6, 2, 0, 0),
            WorkDate = new DateOnly(2026, 9, 5),
            EntryMethod = EntryMethod.Qr,
        });
        db.Context.SaveChanges();

        using var another = db.CreateSeparateContext();
        var saved = another.TimeRecords.Single();

        Assert.Equal(new DateOnly(2026, 9, 5), saved.WorkDate);
        Assert.Equal(6, saved.RecordedAt.Day);
    }

    private static (Guid StoreId, Guid StaffId) SeedStoreAndStaff(TestDatabase db)
    {
        var storeId = Guid.CreateVersion7();
        var staffId = Guid.CreateVersion7();

        db.Context.Stores.Add(new Store
        {
            Id = storeId,
            StoreName = "テスト店",
            CompanyName = "テスト株式会社",
            BusinessDayStart = new TimeOnly(11, 0),
            BusinessDayEnd = new TimeOnly(5, 0),
        });

        db.Context.Staff.Add(new Staff
        {
            Id = staffId,
            StoreId = storeId,
            Name = "山田 太郎",
            Status = StaffStatus.Active,
        });

        db.Context.SaveChanges();

        return (storeId, staffId);
    }

    private static List<string> QueryNames(TestDatabase db, string type)
    {
        using var connection = new SqliteConnection(db.Context.Database.GetConnectionString());
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = $type;";
        command.Parameters.AddWithValue("$type", type);

        var names = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    private static object? ExecuteScalar(TestDatabase db, string sql)
    {
        var connection = db.Context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            db.Context.Database.OpenConnection();
        }

        using var command = connection.CreateCommand();
        command.CommandText = sql;

        return command.ExecuteScalar();
    }
}
