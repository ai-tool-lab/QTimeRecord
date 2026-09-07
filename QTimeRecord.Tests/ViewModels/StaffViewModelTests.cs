using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using QTimeRecord.App.Services;
using QTimeRecord.App.ViewModels;
using QTimeRecord.Core.Domain;
using QTimeRecord.Core.Infrastructure;
using QTimeRecord.Core.Services;

namespace QTimeRecord.Tests.ViewModels;

public sealed class StaffViewModelTests
{
    [Fact]
    public async Task 名簿を読み込むと先頭が選ばれる()
    {
        var harness = new Harness();
        harness.Staff.Items.Add(Item("山田 太郎"));
        harness.Staff.Items.Add(Item("高橋 美咲"));

        var viewModel = harness.Create();
        await viewModel.LoadAsync();

        // 何も選ばれていないと、右側が空のまま「壊れている」ように見える。
        Assert.Equal("山田 太郎", viewModel.SelectedStaff?.Name);
        Assert.Equal("全 2 名", viewModel.CountLabel);
    }

    [Fact]
    public async Task 読み込み直しても選択が保たれる()
    {
        var harness = new Harness();
        harness.Staff.Items.Add(Item("山田 太郎"));
        harness.Staff.Items.Add(Item("高橋 美咲"));

        var viewModel = harness.Create();
        await viewModel.LoadAsync();

        viewModel.SelectedStaff = viewModel.Rows[1];
        await viewModel.LoadAsync();

        // 操作のたびに先頭へ戻ると、続けて直せない。
        Assert.Equal("高橋 美咲", viewModel.SelectedStaff?.Name);
    }

    [Fact]
    public async Task 検索すると絞り込んで読み直す()
    {
        var harness = new Harness();
        harness.Staff.Items.Add(Item("山田 太郎"));

        var viewModel = harness.Create();
        await viewModel.LoadAsync();

        viewModel.Search = "タカハシ";

        Assert.Equal("タカハシ", harness.Staff.LastSearch);
    }

    [Fact]
    public async Task 絞り込みを変えると読み直す()
    {
        var harness = new Harness();
        harness.Staff.Items.Add(Item("山田 太郎"));

        var viewModel = harness.Create();
        await viewModel.LoadAsync();

        viewModel.SelectedFilter = StaffViewModel.Filters[3];

        Assert.Equal(StaffFilter.Retired, harness.Staff.LastFilter);
    }

    [Fact]
    public async Task 選択するとQRカードを描く()
    {
        var harness = new Harness();
        harness.Staff.Items.Add(Item("山田 太郎"));

        var viewModel = harness.Create();
        await viewModel.LoadAsync();

        await harness.SettleAsync();

        Assert.NotNull(viewModel.CardPng);
        Assert.True(viewModel.SaveCardCommand.CanExecute(null));
    }

    [Fact]
    public async Task QRが無いスタッフではカードを出さない()
    {
        var harness = new Harness();
        harness.Staff.Items.Add(Item("伊藤 健治", hasQr: false));
        harness.Cards.HasCard = false;

        var viewModel = harness.Create();
        await viewModel.LoadAsync();

        await harness.SettleAsync();

        Assert.Null(viewModel.CardPng);

        // 保存できないものを押せるようにしない。
        Assert.False(viewModel.SaveCardCommand.CanExecute(null));
    }

    [Fact]
    public async Task 発行状況を表示する()
    {
        var harness = new Harness();
        harness.Staff.Items.Add(Item("山田 太郎", new DateTime(2026, 9, 1, 10, 0, 0)));
        harness.Staff.Items.Add(Item("伊藤 健治", hasQr: false));

        var viewModel = harness.Create();
        await viewModel.LoadAsync();

        Assert.Equal("発行済 (2026/09/01)", viewModel.Rows[0].QrText);
        Assert.Equal("QR無効化済", viewModel.Rows[1].QrText);
    }

    [Fact]
    public async Task 退職への変更は確認を取ってから行う()
    {
        var harness = new Harness();
        harness.Staff.Items.Add(Item("伊藤 健治"));
        harness.Dialogs.ConfirmResult = true;

        var viewModel = harness.Create();
        await viewModel.LoadAsync();

        await viewModel.ChangeStatusAsync(StaffViewModel.StatusOptions[2]);

        Assert.True(harness.Dialogs.ConfirmAsked);
        Assert.Equal(StaffStatus.Retired, harness.Staff.LastStatus);
    }

    [Fact]
    public async Task 退職の確認をやめたら変更しない()
    {
        var harness = new Harness();
        harness.Staff.Items.Add(Item("伊藤 健治"));
        harness.Dialogs.ConfirmResult = false;

        var viewModel = harness.Create();
        await viewModel.LoadAsync();

        await viewModel.ChangeStatusAsync(StaffViewModel.StatusOptions[2]);

        // 配布済みのカードが使えなくなる。確認を通らない経路を作らない。
        Assert.Null(harness.Staff.LastStatus);
    }

    [Fact]
    public async Task 休職への変更は確認を求めない()
    {
        var harness = new Harness();
        harness.Staff.Items.Add(Item("山田 太郎"));

        var viewModel = harness.Create();
        await viewModel.LoadAsync();

        await viewModel.ChangeStatusAsync(StaffViewModel.StatusOptions[1]);

        // QR は失効しないため、戻すのに再発行が要らない。
        Assert.False(harness.Dialogs.ConfirmAsked);
        Assert.Equal(StaffStatus.OnLeave, harness.Staff.LastStatus);
    }

    [Fact]
    public async Task 同じ状態を選び直しても何もしない()
    {
        var harness = new Harness();
        harness.Staff.Items.Add(Item("山田 太郎"));

        var viewModel = harness.Create();
        await viewModel.LoadAsync();

        await viewModel.ChangeStatusAsync(StaffViewModel.StatusOptions[0]);

        Assert.Null(harness.Staff.LastStatus);
    }

    [Fact]
    public async Task 再発行は確認を取ってから行う()
    {
        var harness = new Harness();
        harness.Staff.Items.Add(Item("山田 太郎"));
        harness.Dialogs.ConfirmResult = true;

        var viewModel = harness.Create();
        await viewModel.LoadAsync();

        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)viewModel.ReissueQrCommand)
            .ExecuteAsync(null);

        Assert.True(harness.Dialogs.ConfirmAsked);
        Assert.Equal(1, harness.Staff.ReissueCalls);
    }

    [Fact]
    public async Task 再発行の確認をやめたら発行しない()
    {
        var harness = new Harness();
        harness.Staff.Items.Add(Item("山田 太郎"));
        harness.Dialogs.ConfirmResult = false;

        var viewModel = harness.Create();
        await viewModel.LoadAsync();

        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)viewModel.ReissueQrCommand)
            .ExecuteAsync(null);

        // 押し間違いでカードの配布をやり直すことになる。
        Assert.Equal(0, harness.Staff.ReissueCalls);
    }

    [Fact]
    public async Task 登録を保存したら名簿を読み直す()
    {
        var harness = new Harness();
        harness.Staff.Items.Add(Item("山田 太郎"));
        harness.Editor.Result = true;

        var viewModel = harness.Create();
        await viewModel.LoadAsync();

        var before = harness.Staff.ListCalls;

        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)viewModel.AddCommand)
            .ExecuteAsync(null);

        Assert.Equal(before + 1, harness.Staff.ListCalls);
    }

    [Fact]
    public async Task 保存先を選ばなければ書き出さない()
    {
        var harness = new Harness();
        harness.Staff.Items.Add(Item("山田 太郎"));
        harness.Files.Path = null;

        var viewModel = harness.Create();
        await viewModel.LoadAsync();
        await harness.SettleAsync();

        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)viewModel.SaveCardCommand)
            .ExecuteAsync(null);

        Assert.Empty(harness.Cards.SavedPaths);
    }

    [Fact]
    public async Task 保存に失敗したら成功として見せない()
    {
        var harness = new Harness();
        harness.Staff.Items.Add(Item("山田 太郎"));
        harness.Files.Path = @"C:\dummy\card.png";
        harness.Cards.ThrowOnSave = true;

        var viewModel = harness.Create();
        await viewModel.LoadAsync();
        await harness.SettleAsync();

        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)viewModel.SaveCardCommand)
            .ExecuteAsync(null);

        // 保存できていないのに成功に見せると、印刷しようとして初めて気づく。
        Assert.True(harness.Dialogs.ErrorShown);
        Assert.False(harness.Dialogs.InfoShown);
    }

    [Fact]
    public async Task 保存できたら知らせる()
    {
        var harness = new Harness();
        harness.Staff.Items.Add(Item("山田 太郎"));
        harness.Files.Path = @"C:\dummy\card.png";

        var viewModel = harness.Create();
        await viewModel.LoadAsync();
        await harness.SettleAsync();

        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)viewModel.SaveCardCommand)
            .ExecuteAsync(null);

        Assert.Equal(@"C:\dummy\card.png", Assert.Single(harness.Cards.SavedPaths));
        Assert.True(harness.Dialogs.InfoShown);
    }

    [Fact]
    public async Task 読み込みに失敗したら空の名簿と区別できる()
    {
        var harness = new Harness();
        harness.Staff.Throw = true;

        var viewModel = harness.Create();
        await viewModel.LoadAsync();

        Assert.NotNull(viewModel.ErrorMessage);
        Assert.Empty(viewModel.Rows);
    }

    // ---- 補助 ----

    /// <param name="hasQr">false なら QR 未発行（失効済み）として扱う。</param>
    private static StaffListItem Item(string name, DateTime? issuedAt = null, bool hasQr = true)
        => new(
            new Staff
            {
                Id = Guid.CreateVersion7(),
                StoreId = Guid.CreateVersion7(),
                Name = name,
                Status = StaffStatus.Active,
            },
            hasQr ? issuedAt ?? new DateTime(2026, 9, 1, 10, 0, 0) : null);

    private sealed class Harness
    {
        public StubStaffService Staff { get; } = new();

        public StubCardService Cards { get; } = new();

        public StubStaffEditor Editor { get; } = new();

        public StubDialogs Dialogs { get; } = new();

        public StubFileDialogs Files { get; } = new();

        public StaffViewModel Create() => new(
            Staff,
            Cards,
            Editor,
            Dialogs,
            Files,
            new AppPaths(new AppSettings()),
            NullLogger<StaffViewModel>.Instance);

        /// <summary>カードの描画は選択の変更から非同期で走る。落ち着くまで待つ。</summary>
        public async Task SettleAsync() => await Task.Delay(50);
    }

    private sealed class StubStaffService : IStaffService
    {
        public List<StaffListItem> Items { get; } = [];

        public StaffFilter? LastFilter { get; private set; }

        public string? LastSearch { get; private set; }

        public StaffStatus? LastStatus { get; private set; }

        public int ListCalls { get; private set; }

        public int ReissueCalls { get; private set; }

        public bool Throw { get; set; }

        public Task<IReadOnlyList<StaffListItem>> ListAsync(
            StaffFilter filter = StaffFilter.All,
            string? search = null,
            CancellationToken ct = default)
        {
            LastFilter = filter;
            LastSearch = search;
            ListCalls++;

            return Throw
                ? throw new InvalidOperationException("読み込みに失敗しました。")
                : Task.FromResult<IReadOnlyList<StaffListItem>>(Items);
        }

        public Task<string?> ValidateAsync(StaffDraft draft, CancellationToken ct = default)
            => Task.FromResult<string?>(null);

        public Task<Staff> AddAsync(StaffDraft draft, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<Staff> UpdateAsync(StaffDraft draft, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<Staff> ChangeStatusAsync(
            Guid staffId, StaffStatus status, CancellationToken ct = default)
        {
            LastStatus = status;

            return Task.FromResult(Items[0].Staff);
        }

        public Task<StaffQrToken> ReissueQrAsync(Guid staffId, CancellationToken ct = default)
        {
            ReissueCalls++;

            return Task.FromResult(new StaffQrToken
            {
                Id = Guid.CreateVersion7(),
                StoreId = Guid.CreateVersion7(),
                StaffId = staffId,
                Token = "STUB",
                IssuedAt = DateTime.Now,
            });
        }
    }

    private sealed class StubCardService : IStaffCardService
    {
        public bool HasCard { get; set; } = true;

        public bool ThrowOnSave { get; set; }

        public List<string> SavedPaths { get; } = [];

        public Task<StaffCard?> RenderAsync(Staff staff, CancellationToken ct = default)
            => Task.FromResult(HasCard
                ? new StaffCard([1, 2, 3], new DateOnly(2026, 9, 1))
                : null);

        public Task SaveAsync(byte[] png, string path, CancellationToken ct = default)
        {
            if (ThrowOnSave)
            {
                throw new IOException("保存に失敗しました。");
            }

            SavedPaths.Add(path);

            return Task.CompletedTask;
        }

        public string SuggestFileName(Staff staff) => "QRカード.png";
    }

    private sealed class StubStaffEditor : IStaffEditor
    {
        public bool Result { get; set; }

        public Task<bool> AddAsync() => Task.FromResult(Result);

        public Task<bool> EditAsync(Staff staff) => Task.FromResult(Result);
    }

    private sealed class StubDialogs : IDialogService
    {
        public bool ConfirmResult { get; set; }

        public bool ConfirmAsked { get; private set; }

        public bool InfoShown { get; private set; }

        public bool ErrorShown { get; private set; }

        public bool Confirm(
            string title, string message, string okText = "OK", string cancelText = "キャンセル")
        {
            ConfirmAsked = true;
            return ConfirmResult;
        }

        public void ShowInfo(string title, string message) => InfoShown = true;

        public void ShowError(string title, string message) => ErrorShown = true;
    }

    private sealed class StubFileDialogs : IFileDialogService
    {
        public string? Path { get; set; }

        public string? AskSavePath(
            string title, string suggestedFileName, string filter, string? initialDirectory)
            => Path;

        public void RevealInFolder(string path)
        {
        }
    }
}
