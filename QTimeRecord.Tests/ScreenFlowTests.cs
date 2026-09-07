using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using QTimeRecord.App.Services;
using QTimeRecord.App.ViewModels;
using QTimeRecord.Core.Data;
using QTimeRecord.Core.Data.Repositories;
using QTimeRecord.Core.Devices;
using QTimeRecord.Core.Domain;
using QTimeRecord.Core.Infrastructure;
using QTimeRecord.Core.Services;
using QTimeRecord.Tests.Data;

namespace QTimeRecord.Tests;

/// <summary>
/// QR をかざしてから待機画面へ戻るまでを通しで動かす。
///
/// 個々の ViewModel が正しくても、画面のつなぎ方を間違えると
/// 「打刻したのに戻らない」「他人の画面が残る」といった形で壊れる。
/// そこは単体テストでは見つからないため、ここで実際に繋いで確かめる。
/// </summary>
public sealed class ScreenFlowTests
{
    [Fact]
    public async Task 有効なQRで打刻画面へ進み打刻すると待機へ戻る()
    {
        using var harness = await Harness.CreateAsync();

        await harness.Flow.StartAsync();
        Assert.IsType<IdleViewModel>(harness.Navigation.Current);

        harness.Scanner.RaiseScanned(harness.Token);

        var select = await harness.WaitForAsync<PunchSelectViewModel>();
        Assert.Equal("山田 太郎", select.StaffName);

        await select.PunchAsync(TimeRecordType.ClockIn);

        var result = await harness.WaitForAsync<PunchResultViewModel>();
        Assert.Equal("出勤を記録しました", result.Headline);

        // 打刻は DB に残っていること。画面が進んだだけでは意味がない。
        Assert.Single(harness.Db.CreateSeparateContext().TimeRecords);

        harness.Clock.Advance(IdleTimeoutService.ResultTimeout);
        harness.Ticker.Raise();

        await harness.WaitForAsync<IdleViewModel>();
    }

    [Fact]
    public async Task 四種の打刻がすべてDBに残る()
    {
        using var harness = await Harness.CreateAsync();

        await harness.Flow.StartAsync();
        await harness.WaitForAsync<IdleViewModel>();

        var types = new[]
        {
            TimeRecordType.ClockIn,
            TimeRecordType.BreakStart,
            TimeRecordType.BreakEnd,
            TimeRecordType.ClockOut,
        };

        foreach (var type in types)
        {
            harness.Scanner.RaiseScanned(harness.Token);

            var select = await harness.WaitForAsync<PunchSelectViewModel>();
            await select.PunchAsync(type);

            await harness.WaitForAsync<PunchResultViewModel>();

            harness.Clock.Advance(IdleTimeoutService.ResultTimeout);
            harness.Ticker.Raise();

            await harness.WaitForAsync<IdleViewModel>();

            // 同一種別の60秒ルールに掛からないよう時間を進める。
            harness.Clock.Advance(TimeSpan.FromMinutes(30));
        }

        using var db = harness.Db.CreateSeparateContext();
        var saved = db.TimeRecords.OrderBy(r => r.RecordedAt).ToList();

        Assert.Equal(types, saved.Select(r => r.RecordType));
        Assert.All(saved, r => Assert.Equal(EntryMethod.Qr, r.EntryMethod));

        // 4件とも同じ営業日に入る。
        Assert.Single(saved.Select(r => r.WorkDate).Distinct());
    }

    [Fact]
    public async Task 初回セットアップを終えると待機画面へ進む()
    {
        using var harness = await Harness.CreateAsync(seedStore: false);

        await harness.Flow.StartAsync();

        var setup = await harness.WaitForAsync<SetupViewModel>();

        setup.CompanyName = "テスト株式会社";
        setup.StoreName = "相模原店";
        setup.BusinessDayStart = "09:00";
        setup.BusinessDayEnd = "22:00";
        setup.AdminPin = "12345678";

        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)setup.SubmitCommand)
            .ExecuteAsync(null);

        // 起動 → マイグレーション → 初回セットアップ → 待機（→ plan.md 16-2-6）。
        await harness.WaitForAsync<IdleViewModel>();

        using var db = harness.Db.CreateSeparateContext();
        Assert.Single(db.Stores);
        Assert.Single(db.AdminCredentials);
        Assert.Single(db.DeviceSettings);
    }

    [Fact]
    public async Task 打刻画面で無操作が続くと待機へ戻る()
    {
        using var harness = await Harness.CreateAsync();

        await harness.Flow.StartAsync();
        harness.Scanner.RaiseScanned(harness.Token);
        await harness.WaitForAsync<PunchSelectViewModel>();

        harness.Clock.Advance(IdleTimeoutService.SelectionTimeout);
        harness.Ticker.Raise();

        await harness.WaitForAsync<IdleViewModel>();
        Assert.Empty(harness.Db.CreateSeparateContext().TimeRecords);
    }

    [Fact]
    public async Task 未登録のQRでは待機画面のまま案内を出す()
    {
        using var harness = await Harness.CreateAsync();

        await harness.Flow.StartAsync();
        var idle = Assert.IsType<IdleViewModel>(harness.Navigation.Current);

        harness.Scanner.RaiseScanned("UNKNOWN-TOKEN");
        await harness.SettleAsync();

        Assert.Same(idle, harness.Navigation.Current);
        Assert.NotNull(idle.ErrorMessage);
    }

    [Fact]
    public async Task 打刻画面へ進んだら待機画面は読み取りに反応しない()
    {
        using var harness = await Harness.CreateAsync();

        await harness.Flow.StartAsync();
        harness.Scanner.RaiseScanned(harness.Token);

        var select = await harness.WaitForAsync<PunchSelectViewModel>();

        // 打刻画面を出したまま次の人がかざしても、画面が作り直されてはいけない。
        harness.Scanner.RaiseScanned(harness.Token);
        await harness.SettleAsync();

        Assert.Same(select, harness.Navigation.Current);
    }

    [Fact]
    public async Task 管理者ログインから管理画面へ入りログアウトで待機へ戻る()
    {
        using var harness = await Harness.CreateAsync();

        await harness.Flow.StartAsync();
        var idle = await harness.WaitForAsync<IdleViewModel>();

        idle.AdminLoginCommand.Execute(null);

        var login = await harness.WaitForAsync<AdminLoginViewModel>();
        login.Pin = Harness.AdminPin;
        await login.SubmitAsync();

        var shell = await harness.WaitForAsync<AdminShellViewModel>();
        Assert.Equal("勤務状況", shell.SelectedTab?.Title);

        shell.LogoutCommand.Execute(null);

        await harness.WaitForAsync<IdleViewModel>();
    }

    [Fact]
    public async Task 誤ったPINでは管理画面へ入れない()
    {
        using var harness = await Harness.CreateAsync();

        await harness.Flow.StartAsync();
        (await harness.WaitForAsync<IdleViewModel>()).AdminLoginCommand.Execute(null);

        var login = await harness.WaitForAsync<AdminLoginViewModel>();
        login.Pin = "00000000";
        await login.SubmitAsync();

        await harness.SettleAsync();

        Assert.Same(login, harness.Navigation.Current);
        Assert.NotNull(login.ErrorMessage);
    }

    [Fact]
    public async Task 管理画面にいる間はQRを読んでも打刻されない()
    {
        using var harness = await Harness.CreateAsync();

        await harness.Flow.StartAsync();
        (await harness.WaitForAsync<IdleViewModel>()).AdminLoginCommand.Execute(null);

        var login = await harness.WaitForAsync<AdminLoginViewModel>();
        login.Pin = Harness.AdminPin;
        await login.SubmitAsync();

        var shell = await harness.WaitForAsync<AdminShellViewModel>();

        harness.Scanner.RaiseScanned(harness.Token);
        await harness.SettleAsync();

        // 管理者の操作中に別の人の打刻が入ると、どちらの操作か分からなくなる。
        Assert.Same(shell, harness.Navigation.Current);
        Assert.Empty(harness.Db.CreateSeparateContext().TimeRecords);
    }

    [Fact]
    public async Task 再発行すると旧QRでは打刻画面へ進めない()
    {
        using var harness = await Harness.CreateAsync();

        await harness.Flow.StartAsync();
        var idle = await harness.WaitForAsync<IdleViewModel>();

        // 旧カードを紛失したので再発行した、という状況。
        await harness.Staff.ReissueQrAsync(harness.StaffId);

        harness.Scanner.RaiseScanned(harness.Token);
        await harness.SettleAsync();

        // 紛失したカードで打刻できたままでは、再発行の意味がない。
        Assert.Same(idle, harness.Navigation.Current);
        Assert.NotNull(idle.ErrorMessage);
        Assert.Empty(harness.Db.CreateSeparateContext().TimeRecords);
    }

    [Fact]
    public async Task 退職にすると打刻画面へ進めない()
    {
        using var harness = await Harness.CreateAsync();

        await harness.Flow.StartAsync();
        var idle = await harness.WaitForAsync<IdleViewModel>();

        await harness.Staff.ChangeStatusAsync(harness.StaffId, StaffStatus.Retired);

        harness.Scanner.RaiseScanned(harness.Token);
        await harness.SettleAsync();

        Assert.Same(idle, harness.Navigation.Current);
        Assert.NotNull(idle.ErrorMessage);
    }

    [Fact]
    public async Task お知らせを保存すると待機画面に出る()
    {
        using var harness = await Harness.CreateAsync();

        await harness.Flow.StartAsync();

        var idle = await harness.WaitForAsync<IdleViewModel>();
        Assert.Empty(idle.Announcements);

        // 管理画面を通さず、設定サービスで直接保存する。
        // ここで見たいのは「保存したものが待機画面に出るか」だけ。
        await harness.Settings.SaveAsync(new StoreSettingsDraft
        {
            StoreName = "相模原店",
            BusinessDayStart = new TimeOnly(9, 0),
            BusinessDayEnd = new TimeOnly(22, 0),
            AnnouncementTitle = "【店舗連絡】今週の重要共有事項",
            Announcements =
            [
                new("健康診断", "受診希望日を月末までに提出願います。"),
                new("衛生管理", "検温と手指アルコール消毒を励行してください。"),
            ],
        });

        // 待機画面はログアウト時などに作り直される。作り直せば新しい内容を読む。
        await harness.Flow.StartAsync();
        var reloaded = await harness.WaitForAsync<IdleViewModel>();

        Assert.Equal("【店舗連絡】今週の重要共有事項", reloaded.AnnouncementTitle);
        Assert.Equal(2, reloaded.Announcements.Count);
        Assert.Equal("健康診断", reloaded.Announcements[0].Heading);
        Assert.True(reloaded.HasAnnouncements);
    }

    [Fact]
    public async Task 未セットアップならセットアップ画面から始まる()
    {
        using var harness = await Harness.CreateAsync(seedStore: false);

        await harness.Flow.StartAsync();

        Assert.IsType<SetupViewModel>(harness.Navigation.Current);
    }

    // ---- 補助 ----

    private sealed class Harness : IDisposable
    {
        public const string AdminPin = "12345678";

        private Harness(TestDatabase db, ServiceProvider provider, string token, Guid staffId)
        {
            Db = db;
            Provider = provider;
            Token = token;
            StaffId = staffId;
            Staff = provider.GetRequiredService<IStaffService>();

            Clock = (TestClock)provider.GetRequiredService<IClock>();
            Ticker = (StubTicker)provider.GetRequiredService<IUiTicker>();
            Scanner = (StubScanner)provider.GetRequiredService<IQrScannerService>();
            Navigation = provider.GetRequiredService<INavigationService>();
            Flow = provider.GetRequiredService<ScreenFlow>();
        }

        public TestDatabase Db { get; }

        public TestClock Clock { get; }

        public StubTicker Ticker { get; }

        public StubScanner Scanner { get; }

        public INavigationService Navigation { get; }

        public ScreenFlow Flow { get; }

        public string Token { get; }

        /// <summary>打刻できるスタッフ。再発行や退職の確認に使う。</summary>
        public Guid StaffId { get; }

        public IStaffService Staff { get; }

        public IStoreSettingsService Settings => Provider.GetRequiredService<IStoreSettingsService>();

        private ServiceProvider Provider { get; }

        public static async Task<Harness> CreateAsync(bool seedStore = true)
        {
            var db = new TestDatabase();
            db.Migrate();

            var services = new ServiceCollection();

            services.AddSingleton(db.Factory);
            services.AddSingleton<IClock>(new TestClock(new DateTime(2026, 9, 6, 13, 0, 0)));
            services.AddSingleton<IUiTicker>(new StubTicker());
            services.AddSingleton<IQrScannerService>(new StubScanner());

            services.AddSingleton<IStoreRepository, StoreRepository>();
            services.AddSingleton<IStaffRepository, StaffRepository>();
            services.AddSingleton<IQrTokenRepository, QrTokenRepository>();
            services.AddSingleton<ITimeRecordRepository, TimeRecordRepository>();
            services.AddSingleton<IDeviceSettingsRepository, DeviceSettingsRepository>();
            services.AddSingleton<IAdminCredentialRepository, AdminCredentialRepository>();

            services.AddSingleton<IPasswordHasher, PasswordHasher>();
            services.AddSingleton<IQrTokenService, QrTokenService>();
            services.AddSingleton<IStoreSetupService, StoreSetupService>();
            services.AddSingleton<IPunchService, PunchService>();
            services.AddSingleton<IAdminAuthService, AdminAuthService>();
            services.AddSingleton<IStaffService, StaffService>();
            services.AddSingleton<IStoreSettingsService, StoreSettingsService>();

            services.AddSingleton<INavigationService, NavigationService>();
            services.AddSingleton<IDialogService, StubDialogService>();
            services.AddSingleton<ScannerHost>();

            // 管理画面の中身までは通しで見ない。タブの枠だけ用意する。
            services.AddSingleton<IAdminTabProvider, StubTabProvider>();
            services.AddSingleton<ScreenFlow>();

            services.AddTransient<IdleTimeoutService>();
            services.AddTransient<SetupViewModel>();
            services.AddTransient<IdleViewModel>();
            services.AddTransient<PunchSelectViewModel>();
            services.AddTransient<PunchResultViewModel>();
            services.AddTransient<AdminLoginViewModel>();
            services.AddTransient<AdminShellViewModel>();

            services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

            var provider = services.BuildServiceProvider();
            var token = string.Empty;
            var staffId = Guid.Empty;

            if (seedStore)
            {
                (token, staffId) = await SeedAsync(provider);
            }

            return new Harness(db, provider, token, staffId);
        }

        /// <summary>
        /// 画面が切り替わるまで待つ。
        ///
        /// 遷移はイベント経由で非同期に走り、途中で DB も読む。
        /// 決め打ちの待ち時間にすると、遅い環境で不安定なテストになる。
        /// </summary>
        public async Task<T> WaitForAsync<T>()
            where T : class
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);

            while (DateTime.UtcNow < deadline)
            {
                if (Navigation.Current is T view)
                {
                    return view;
                }

                await Task.Delay(10);
            }

            throw new TimeoutException(
                $"{typeof(T).Name} へ遷移しませんでした（現在: {Navigation.Current?.GetType().Name}）。");
        }

        /// <summary>遷移しないことを確かめる。起きないことの確認なので、少しだけ待つ。</summary>
        public async Task SettleAsync() => await Task.Delay(100);

        public void Dispose()
        {
            Provider.Dispose();
            Db.Dispose();
        }

        private static async Task<(string Token, Guid StaffId)> SeedAsync(IServiceProvider provider)
        {
            var store = await provider.GetRequiredService<IStoreSetupService>().InitializeAsync(
                new StoreSetupRequest
                {
                    CompanyName = "テスト株式会社",
                    StoreName = "相模原店",
                    BusinessDayStart = new TimeOnly(9, 0),
                    BusinessDayEnd = new TimeOnly(22, 0),
                    AdminPin = AdminPin,
                });

            var staff = new Staff
            {
                Id = Guid.CreateVersion7(),
                StoreId = store.Id,
                Name = "山田 太郎",
                Status = StaffStatus.Active,
            };

            await provider.GetRequiredService<IStaffRepository>().AddAsync(staff);

            var issued = await provider.GetRequiredService<IQrTokenService>()
                .IssueAsync(store.Id, staff.Id);

            return (issued.Token, staff.Id);
        }
    }

    /// <summary>
    /// タブの中身は仮のものにする。ここで確かめたいのは画面の並び順で、
    /// 管理画面の各タブ自体は 11〜13章のテストで見る。
    /// </summary>
    private sealed class StubTabProvider : IAdminTabProvider
    {
        public IReadOnlyList<AdminTab> Create() =>
        [
            new("勤務状況", () => new AdminPlaceholder("勤務状況", "仮")),
            new("スタッフ管理", () => new AdminPlaceholder("スタッフ管理", "仮")),
            new("店舗設定", () => new AdminPlaceholder("店舗設定", "仮")),
        ];
    }

    private sealed class StubDialogService : IDialogService
    {
        public bool Confirm(
            string title, string message, string okText = "OK", string cancelText = "キャンセル")
            => true;

        public void ShowInfo(string title, string message)
        {
        }

        public void ShowError(string title, string message)
        {
        }
    }

    private sealed class StubScanner : IQrScannerService
    {
        public ScannerState State { get; private set; } = ScannerState.Connected;

        public event EventHandler<string>? Scanned;

        public event EventHandler<ScannerState>? StateChanged;

        public event EventHandler? ScanFailed;

        public void RaiseScanFailed() => ScanFailed?.Invoke(this, EventArgs.Empty);

        public void Start(SerialPortSettings settings)
        {
        }

        public void Stop()
        {
        }

        public void Tick()
        {
        }

        public Task<ConnectionTestResult> TestConnectionAsync(
            SerialPortSettings settings, TimeSpan timeout, CancellationToken ct = default)
            => Task.FromResult(new ConnectionTestResult(true, "ok"));

        public void Dispose()
        {
        }

        public void RaiseScanned(string token) => Scanned?.Invoke(this, token);

        public void RaiseStateChanged(ScannerState state)
        {
            State = state;
            StateChanged?.Invoke(this, state);
        }
    }
}
