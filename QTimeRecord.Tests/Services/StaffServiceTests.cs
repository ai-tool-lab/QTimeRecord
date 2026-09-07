using Microsoft.Extensions.Logging.Abstractions;
using QTimeRecord.Core.Data.Repositories;
using QTimeRecord.Core.Domain;
using QTimeRecord.Core.Infrastructure;
using QTimeRecord.Core.Services;
using QTimeRecord.Tests.Data;

namespace QTimeRecord.Tests.Services;

public sealed class StaffServiceTests
{
    [Fact]
    public async Task 登録するとQRも同時に発行される()
    {
        using var db = new TestDatabase();
        var fixture = await Fixture.CreateAsync(db);

        var staff = await fixture.Service.AddAsync(Draft("山田 太郎"));

        // あとで発行する運用にすると、カードを渡し忘れたスタッフが打刻できないまま出勤する。
        var token = await fixture.Tokens.GetActiveForStaffAsync(staff.Id);

        Assert.NotNull(token);
        Assert.Equal(staff.Id, token.StaffId);
    }

    [Fact]
    public async Task 発行したQRで打刻対象として解決できる()
    {
        using var db = new TestDatabase();
        var fixture = await Fixture.CreateAsync(db);

        var staff = await fixture.Service.AddAsync(Draft("山田 太郎"));
        var token = await fixture.Tokens.GetActiveForStaffAsync(staff.Id);

        var resolved = await fixture.QrTokens.ResolveAsync(token!.Token);

        Assert.True(resolved.CanPunch);
        Assert.Equal(staff.Id, resolved.Staff!.Id);
    }

    [Fact]
    public async Task 氏名が空なら登録できない()
    {
        using var db = new TestDatabase();
        var fixture = await Fixture.CreateAsync(db);

        Assert.NotNull(await fixture.Service.ValidateAsync(Draft("   ")));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.AddAsync(Draft("   ")));
    }

    [Fact]
    public async Task 社員番号は重複させない()
    {
        using var db = new TestDatabase();
        var fixture = await Fixture.CreateAsync(db);

        await fixture.Service.AddAsync(Draft("山田 太郎", staffNo: "E-0104"));

        // 重複すると、CSV を給与システムへ渡したときに別人の勤怠が混ざる。
        Assert.NotNull(await fixture.Service.ValidateAsync(Draft("高橋 美咲", staffNo: "E-0104")));

        await Assert.ThrowsAsync<ArgumentException>(
            () => fixture.Service.AddAsync(Draft("高橋 美咲", staffNo: "E-0104")));
    }

    [Fact]
    public async Task 編集では自分の社員番号を重複とみなさない()
    {
        using var db = new TestDatabase();
        var fixture = await Fixture.CreateAsync(db);

        var staff = await fixture.Service.AddAsync(Draft("山田 太郎", staffNo: "E-0104"));

        var draft = Draft("山田 太郎", staffNo: "E-0104") with { StaffId = staff.Id };

        Assert.Null(await fixture.Service.ValidateAsync(draft));
    }

    [Fact]
    public async Task 社員番号が空なら重複を見ない()
    {
        using var db = new TestDatabase();
        var fixture = await Fixture.CreateAsync(db);

        await fixture.Service.AddAsync(Draft("山田 太郎"));

        // 未設定を「同じ番号」と扱うと、2人目以降を登録できなくなる。
        Assert.Null(await fixture.Service.ValidateAsync(Draft("高橋 美咲")));
    }

    [Fact]
    public async Task 編集しても登録済みのQRは変わらない()
    {
        using var db = new TestDatabase();
        var fixture = await Fixture.CreateAsync(db);

        var staff = await fixture.Service.AddAsync(Draft("山田 太郎"));
        var before = await fixture.Tokens.GetActiveForStaffAsync(staff.Id);

        await fixture.Service.UpdateAsync(
            Draft("山田 太郎", nameKana: "ヤマダ タロウ") with { StaffId = staff.Id });

        var after = await fixture.Tokens.GetActiveForStaffAsync(staff.Id);

        // 氏名を直しただけで配布済みのカードが使えなくなってはいけない。
        Assert.Equal(before!.Token, after!.Token);
    }

    [Fact]
    public async Task 退職にするとQRが失効する()
    {
        using var db = new TestDatabase();
        var fixture = await Fixture.CreateAsync(db);

        var staff = await fixture.Service.AddAsync(Draft("山田 太郎"));
        var token = await fixture.Tokens.GetActiveForStaffAsync(staff.Id);

        await fixture.Service.ChangeStatusAsync(staff.Id, StaffStatus.Retired);

        // 手で失効させる運用にすると、忘れたときに退職者のQRで打刻できてしまう（→ plan.md Q15）。
        Assert.Null(await fixture.Tokens.GetActiveForStaffAsync(staff.Id));

        var resolved = await fixture.QrTokens.ResolveAsync(token!.Token);
        Assert.False(resolved.CanPunch);
    }

    [Fact]
    public async Task 休職にしてもQRは失効させない()
    {
        using var db = new TestDatabase();
        var fixture = await Fixture.CreateAsync(db);

        var staff = await fixture.Service.AddAsync(Draft("山田 太郎"));

        await fixture.Service.ChangeStatusAsync(staff.Id, StaffStatus.OnLeave);

        // 復職のたびにカードを刷り直すことになる。打刻は在籍状態で止める。
        Assert.NotNull(await fixture.Tokens.GetActiveForStaffAsync(staff.Id));

        var token = await fixture.Tokens.GetActiveForStaffAsync(staff.Id);
        var resolved = await fixture.QrTokens.ResolveAsync(token!.Token);

        Assert.False(resolved.CanPunch);
        Assert.Equal(QrResolution.OnLeave, resolved.Resolution);
    }

    [Fact]
    public async Task 再発行すると旧QRでは打刻できない()
    {
        using var db = new TestDatabase();
        var fixture = await Fixture.CreateAsync(db);

        var staff = await fixture.Service.AddAsync(Draft("山田 太郎"));
        var old = await fixture.Tokens.GetActiveForStaffAsync(staff.Id);

        var reissued = await fixture.Service.ReissueQrAsync(staff.Id);

        Assert.NotEqual(old!.Token, reissued.Token);

        // 紛失したカードで打刻できたままでは、再発行の意味がない。
        Assert.False((await fixture.QrTokens.ResolveAsync(old.Token)).CanPunch);
        Assert.True((await fixture.QrTokens.ResolveAsync(reissued.Token)).CanPunch);
    }

    [Fact]
    public async Task 名簿は発行状況を添えて返す()
    {
        using var db = new TestDatabase();
        var fixture = await Fixture.CreateAsync(db);

        var staff = await fixture.Service.AddAsync(Draft("山田 太郎"));

        var withQr = Assert.Single(await fixture.Service.ListAsync());
        Assert.True(withQr.HasActiveQr);

        await fixture.Service.ChangeStatusAsync(staff.Id, StaffStatus.Retired);

        var withoutQr = Assert.Single(await fixture.Service.ListAsync());
        Assert.False(withoutQr.HasActiveQr);
    }

    [Fact]
    public async Task フリガナで検索できる()
    {
        using var db = new TestDatabase();
        var fixture = await Fixture.CreateAsync(db);

        await fixture.Service.AddAsync(Draft("山田 太郎", nameKana: "ヤマダ タロウ"));
        await fixture.Service.AddAsync(Draft("高橋 美咲", nameKana: "タカハシ ミサキ"));

        var found = await fixture.Service.ListAsync(search: "タカハシ");

        Assert.Equal("高橋 美咲", Assert.Single(found).Staff.Name);
    }

    [Fact]
    public async Task 社員番号でも検索できる()
    {
        using var db = new TestDatabase();
        var fixture = await Fixture.CreateAsync(db);

        await fixture.Service.AddAsync(Draft("山田 太郎", staffNo: "E-0104"));
        await fixture.Service.AddAsync(Draft("高橋 美咲", staffNo: "P-0210"));

        var found = await fixture.Service.ListAsync(search: "P-02");

        Assert.Equal("高橋 美咲", Assert.Single(found).Staff.Name);
    }

    [Theory]
    [InlineData(StaffFilter.FullTime, "山田 太郎")]
    [InlineData(StaffFilter.PartTime, "高橋 美咲")]
    public async Task 区分で絞り込める(StaffFilter filter, string expected)
    {
        using var db = new TestDatabase();
        var fixture = await Fixture.CreateAsync(db);

        await fixture.Service.AddAsync(Draft("山田 太郎", employmentType: "社員"));
        await fixture.Service.AddAsync(Draft("高橋 美咲", employmentType: "パート"));

        var found = await fixture.Service.ListAsync(filter);

        Assert.Equal(expected, Assert.Single(found).Staff.Name);
    }

    [Fact]
    public async Task 退職者だけを絞り込める()
    {
        using var db = new TestDatabase();
        var fixture = await Fixture.CreateAsync(db);

        await fixture.Service.AddAsync(Draft("山田 太郎"));
        var retired = await fixture.Service.AddAsync(Draft("伊藤 健治"));

        await fixture.Service.ChangeStatusAsync(retired.Id, StaffStatus.Retired);

        Assert.Equal(2, (await fixture.Service.ListAsync()).Count);
        Assert.Equal("伊藤 健治",
            Assert.Single(await fixture.Service.ListAsync(StaffFilter.Retired)).Staff.Name);
    }

    [Fact]
    public async Task 退職しても名簿から消えない()
    {
        using var db = new TestDatabase();
        var fixture = await Fixture.CreateAsync(db);

        var staff = await fixture.Service.AddAsync(Draft("伊藤 健治"));
        await fixture.Service.ChangeStatusAsync(staff.Id, StaffStatus.Retired);

        // 物理削除すると、その人の過去の勤務記録が壊れる（→ CLAUDE.md 必須ルール7）。
        Assert.Single(db.CreateSeparateContext().Staff);
        Assert.Single(await fixture.Service.ListAsync());
    }

    [Fact]
    public async Task 前後の空白は取り除いて保存する()
    {
        using var db = new TestDatabase();
        var fixture = await Fixture.CreateAsync(db);

        var staff = await fixture.Service.AddAsync(
            Draft("  山田 太郎  ", staffNo: "  E-0104  ", nameKana: "   "));

        Assert.Equal("山田 太郎", staff.Name);
        Assert.Equal("E-0104", staff.StaffNo);

        // 空白だけのフリガナを残すと、並び替えで先頭に来てしまう。
        Assert.Null(staff.NameKana);
    }

    // ---- 補助 ----

    private static StaffDraft Draft(
        string name,
        string? staffNo = null,
        string? nameKana = null,
        string? employmentType = null) => new()
    {
        Name = name,
        StaffNo = staffNo,
        NameKana = nameKana,
        EmploymentType = employmentType,
    };

    private sealed class Fixture
    {
        private Fixture(TestDatabase db)
        {
            Tokens = new QrTokenRepository(db.Factory);
            QrTokens = new QrTokenService(Tokens, new SystemClock());

            Service = new StaffService(
                new StoreRepository(db.Factory),
                new StaffRepository(db.Factory),
                Tokens,
                QrTokens,
                NullLogger<StaffService>.Instance);
        }

        public IQrTokenRepository Tokens { get; }

        public IQrTokenService QrTokens { get; }

        public IStaffService Service { get; }

        public static async Task<Fixture> CreateAsync(TestDatabase db)
        {
            db.Migrate();

            var setup = new StoreSetupService(
                new StoreRepository(db.Factory),
                new AdminCredentialRepository(db.Factory),
                new DeviceSettingsRepository(db.Factory),
                new PasswordHasher());

            await setup.InitializeAsync(new StoreSetupRequest
            {
                CompanyName = "テスト株式会社",
                StoreName = "相模原店",
                BusinessDayStart = new TimeOnly(9, 0),
                BusinessDayEnd = new TimeOnly(22, 0),
                AdminPin = "12345678",
            });

            return new Fixture(db);
        }
    }
}
