using QTimeRecord.Core.Data.Repositories;
using QTimeRecord.Core.Domain;

namespace QTimeRecord.Tests.Data;

/// <summary>
/// Repository の CRUD と、間違えると静かに壊れる検索条件を確認する。
/// </summary>
public sealed class RepositoryTests
{
    private static readonly DateTime Now =
        new(2026, 9, 5, 12, 0, 0);

    // ---- スタッフ ----

    [Fact]
    public async Task 名簿は在籍状態で絞り込める()
    {
        using var db = CreateDatabase(out var storeId);
        var repository = new StaffRepository(db.Factory);

        await repository.AddAsync(NewStaff(storeId, "山田 太郎", "ヤマダ タロウ", "E-0001"));
        await repository.AddAsync(NewStaff(storeId, "佐藤 花子", "サトウ ハナコ", "E-0002", StaffStatus.Retired));

        var active = await repository.ListAsync(storeId, StaffStatus.Active);

        Assert.Single(active);
        Assert.Equal("山田 太郎", active[0].Name);
    }

    [Fact]
    public async Task 名簿は氏名フリガナ社員番号で検索できる()
    {
        using var db = CreateDatabase(out var storeId);
        var repository = new StaffRepository(db.Factory);

        await repository.AddAsync(NewStaff(storeId, "山田 太郎", "ヤマダ タロウ", "E-0001"));
        await repository.AddAsync(NewStaff(storeId, "佐藤 花子", "サトウ ハナコ", "P-0210"));

        Assert.Single(await repository.ListAsync(storeId, search: "山田"));
        Assert.Single(await repository.ListAsync(storeId, search: "サトウ"));
        Assert.Single(await repository.ListAsync(storeId, search: "P-0210"));
        Assert.Empty(await repository.ListAsync(storeId, search: "存在しない"));
    }

    [Fact]
    public async Task 社員番号の重複を検出する()
    {
        using var db = CreateDatabase(out var storeId);
        var repository = new StaffRepository(db.Factory);

        var staff = NewStaff(storeId, "山田 太郎", "ヤマダ タロウ", "E-0001");
        await repository.AddAsync(staff);

        Assert.True(await repository.StaffNoExistsAsync(storeId, "E-0001"));
        Assert.False(await repository.StaffNoExistsAsync(storeId, "E-0002"));

        // 編集中の本人は重複扱いにしない
        Assert.False(await repository.StaffNoExistsAsync(storeId, "E-0001", excludeStaffId: staff.Id));
    }

    // ---- QR トークン ----

    [Fact]
    public async Task 再発行すると古いトークンが失効する()
    {
        using var db = CreateDatabase(out var storeId);
        var staffRepository = new StaffRepository(db.Factory);
        var tokenRepository = new QrTokenRepository(db.Factory);

        var staff = NewStaff(storeId, "山田 太郎", "ヤマダ タロウ", "E-0001");
        await staffRepository.AddAsync(staff);

        await tokenRepository.IssueAsync(storeId, staff.Id, "OLD-TOKEN", Now);
        await tokenRepository.IssueAsync(storeId, staff.Id, "NEW-TOKEN", Now.AddDays(1));

        var active = await tokenRepository.GetActiveForStaffAsync(staff.Id);
        Assert.NotNull(active);
        Assert.Equal("NEW-TOKEN", active.Token);

        // 旧トークンは残るが失効している。「無効なQR」と案内するために行自体は消さない。
        var old = await tokenRepository.FindByTokenAsync("OLD-TOKEN");
        Assert.NotNull(old);
        Assert.False(old.IsActive);

        Assert.Equal(2, (await tokenRepository.ListForStaffAsync(staff.Id)).Count);
    }

    [Fact]
    public async Task 失効させると有効なトークンが無くなる()
    {
        using var db = CreateDatabase(out var storeId);
        var staffRepository = new StaffRepository(db.Factory);
        var tokenRepository = new QrTokenRepository(db.Factory);

        var staff = NewStaff(storeId, "山田 太郎", "ヤマダ タロウ", "E-0001");
        await staffRepository.AddAsync(staff);
        await tokenRepository.IssueAsync(storeId, staff.Id, "TOKEN", Now);

        var revoked = await tokenRepository.RevokeActiveAsync(staff.Id, Now.AddHours(1));

        Assert.Equal(1, revoked);
        Assert.Null(await tokenRepository.GetActiveForStaffAsync(staff.Id));
    }

    [Fact]
    public async Task 未登録のトークンはnullを返す()
    {
        using var db = CreateDatabase(out _);
        var tokenRepository = new QrTokenRepository(db.Factory);

        Assert.Null(await tokenRepository.FindByTokenAsync("UNKNOWN"));
    }

    // ---- 打刻 ----

    [Fact]
    public async Task 月次取得は月初と月末を含む()
    {
        using var db = CreateDatabase(out var storeId);
        var staffRepository = new StaffRepository(db.Factory);
        var recordRepository = new TimeRecordRepository(db.Factory);

        var staff = NewStaff(storeId, "山田 太郎", "ヤマダ タロウ", "E-0001");
        await staffRepository.AddAsync(staff);

        // 境界: 8/31（前月）/ 9/1（月初）/ 9/30（月末）/ 10/1（翌月）
        foreach (var date in new[]
                 {
                     new DateOnly(2026, 8, 31),
                     new DateOnly(2026, 9, 1),
                     new DateOnly(2026, 9, 30),
                     new DateOnly(2026, 10, 1),
                 })
        {
            await recordRepository.AddAsync(NewRecord(storeId, staff.Id, date, TimeRecordType.ClockIn));
        }

        var september = await recordRepository.ListByMonthAsync(storeId, 2026, 9);

        Assert.Equal(2, september.Count);
        Assert.Equal(new DateOnly(2026, 9, 1), september[0].WorkDate);
        Assert.Equal(new DateOnly(2026, 9, 30), september[1].WorkDate);
    }

    [Fact]
    public async Task 未退勤の出勤を検出する()
    {
        using var db = CreateDatabase(out var storeId);
        var staffRepository = new StaffRepository(db.Factory);
        var recordRepository = new TimeRecordRepository(db.Factory);

        var staff = NewStaff(storeId, "山田 太郎", "ヤマダ タロウ", "E-0001");
        await staffRepository.AddAsync(staff);

        var workDate = new DateOnly(2026, 9, 5);
        await recordRepository.AddAsync(
            NewRecord(storeId, staff.Id, workDate, TimeRecordType.ClockIn, Now.AddHours(10)));

        var open = await recordRepository.GetOpenClockInAsync(staff.Id);
        Assert.NotNull(open);

        // 退勤したら「未退勤」ではなくなる
        await recordRepository.AddAsync(
            NewRecord(storeId, staff.Id, workDate, TimeRecordType.ClockOut, Now.AddHours(18)));

        Assert.Null(await recordRepository.GetOpenClockInAsync(staff.Id));
    }

    [Fact]
    public async Task 中抜けは未退勤の判定に影響しない()
    {
        using var db = CreateDatabase(out var storeId);
        var staffRepository = new StaffRepository(db.Factory);
        var recordRepository = new TimeRecordRepository(db.Factory);

        var staff = NewStaff(storeId, "山田 太郎", "ヤマダ タロウ", "E-0001");
        await staffRepository.AddAsync(staff);

        var workDate = new DateOnly(2026, 9, 5);
        await recordRepository.AddAsync(
            NewRecord(storeId, staff.Id, workDate, TimeRecordType.ClockIn, Now.AddHours(10)));
        await recordRepository.AddAsync(
            NewRecord(storeId, staff.Id, workDate, TimeRecordType.BreakStart, Now.AddHours(13)));

        // 中抜け中でも「出勤したまま」であることに変わりはない
        Assert.NotNull(await recordRepository.GetOpenClockInAsync(staff.Id));
    }

    [Fact]
    public async Task 打刻を修正して削除できる()
    {
        using var db = CreateDatabase(out var storeId);
        var staffRepository = new StaffRepository(db.Factory);
        var recordRepository = new TimeRecordRepository(db.Factory);

        var staff = NewStaff(storeId, "山田 太郎", "ヤマダ タロウ", "E-0001");
        await staffRepository.AddAsync(staff);

        var record = NewRecord(storeId, staff.Id, new DateOnly(2026, 9, 5), TimeRecordType.ClockIn);
        await recordRepository.AddAsync(record);

        record.RecordedAt = Now.AddHours(-1);
        record.EntryMethod = EntryMethod.ManualEdit;
        record.Note = "打刻漏れのため代理入力";
        await recordRepository.UpdateAsync(record);

        var updated = await recordRepository.GetByIdAsync(record.Id);
        Assert.NotNull(updated);
        Assert.Equal(EntryMethod.ManualEdit, updated.EntryMethod);
        Assert.Equal("打刻漏れのため代理入力", updated.Note);

        await recordRepository.DeleteAsync(record.Id);
        Assert.Null(await recordRepository.GetByIdAsync(record.Id));
    }

    // ---- お知らせ ----

    [Fact]
    public async Task お知らせは置き換えで表示順が振り直される()
    {
        using var db = CreateDatabase(out var storeId);
        var repository = new StoreRepository(db.Factory);

        await repository.ReplaceAnnouncementsAsync(storeId,
        [
            NewAnnouncement("健康診断", "受診希望日を月末までに提出してください。"),
            NewAnnouncement("衛生管理", "検温と手指消毒を励行してください。"),
        ]);

        var saved = await repository.GetAnnouncementsAsync(storeId);

        Assert.Equal(2, saved.Count);
        Assert.Equal(1, saved[0].DisplayOrder);
        Assert.Equal("健康診断", saved[0].Heading);
        Assert.Equal(2, saved[1].DisplayOrder);

        // 置き換えなので、前回の内容は残らない
        await repository.ReplaceAnnouncementsAsync(storeId, [NewAnnouncement("シフト", "今週日曜締切です。")]);

        var replaced = await repository.GetAnnouncementsAsync(storeId);
        Assert.Single(replaced);
        Assert.Equal("シフト", replaced[0].Heading);
    }

    [Fact]
    public async Task お知らせは5件を超えると拒否される()
    {
        using var db = CreateDatabase(out var storeId);
        var repository = new StoreRepository(db.Factory);

        var six = Enumerable.Range(1, 6).Select(i => NewAnnouncement($"見出し{i}", $"本文{i}")).ToList();

        await Assert.ThrowsAsync<ArgumentException>(
            () => repository.ReplaceAnnouncementsAsync(storeId, six));
    }

    // ---- 設定 ----

    [Fact]
    public async Task シリアル設定は保存して読み直せる()
    {
        using var db = CreateDatabase(out var storeId);
        var repository = new DeviceSettingsRepository(db.Factory);

        await repository.SaveAsync(new DeviceSettings
        {
            StoreId = storeId,
            ComPort = "COM3",
            BaudRate = 19200,
        });

        var saved = await repository.GetAsync(storeId);
        Assert.NotNull(saved);
        Assert.Equal("COM3", saved.ComPort);
        Assert.Equal(19200, saved.BaudRate);

        // 2回目は更新になる（重複行を作らない）
        saved.ComPort = "COM5";
        await repository.SaveAsync(saved);

        Assert.Equal("COM5", (await repository.GetAsync(storeId))!.ComPort);
    }

    // ---- 補助 ----

    private static TestDatabase CreateDatabase(out Guid storeId)
    {
        var db = new TestDatabase();
        db.Migrate();

        storeId = Guid.CreateVersion7();
        db.Context.Stores.Add(new Store
        {
            Id = storeId,
            StoreName = "テスト店",
            CompanyName = "テスト株式会社",
            BusinessDayStart = new TimeOnly(11, 0),
            BusinessDayEnd = new TimeOnly(5, 0),
            CreatedAt = Now,
            UpdatedAt = Now,
        });
        db.Context.SaveChanges();

        return db;
    }

    private static Staff NewStaff(
        Guid storeId, string name, string kana, string staffNo, StaffStatus status = StaffStatus.Active)
        => new()
        {
            Id = Guid.CreateVersion7(),
            StoreId = storeId,
            Name = name,
            NameKana = kana,
            StaffNo = staffNo,
            Status = status,
        };

    private static TimeRecord NewRecord(
        Guid storeId, Guid staffId, DateOnly workDate, TimeRecordType type, DateTime? recordedAt = null)
        => new()
        {
            Id = Guid.CreateVersion7(),
            StoreId = storeId,
            StaffId = staffId,
            RecordType = type,
            RecordedAt = recordedAt ?? workDate.ToDateTime(new TimeOnly(11, 0)).ToLocalTime(),
            WorkDate = workDate,
            EntryMethod = EntryMethod.Qr,
        };

    private static Announcement NewAnnouncement(string heading, string body)
        => new()
        {
            Id = Guid.CreateVersion7(),
            StoreId = Guid.Empty,
            DisplayOrder = 0,
            Heading = heading,
            Body = body,
        };
}
