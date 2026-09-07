using QTimeRecord.App.ViewModels;
using QTimeRecord.Core.Domain;
using QTimeRecord.Core.Services;

namespace QTimeRecord.Tests.ViewModels;

public sealed class StaffEditViewModelTests
{
    [Fact]
    public void 新規登録ではQR発行を予告する()
    {
        var (viewModel, _) = Create();

        viewModel.OpenForAdd();

        Assert.True(viewModel.IsNew);
        Assert.Equal("スタッフを登録", viewModel.Title);

        // 登録と同時に発行されることを知らせないと、カードを渡し忘れる。
        Assert.NotNull(viewModel.IssueNote);
    }

    [Fact]
    public void 編集では既存の値が入る()
    {
        var (viewModel, _) = Create();

        viewModel.OpenForEdit(new Staff
        {
            Id = Guid.CreateVersion7(),
            StoreId = Guid.CreateVersion7(),
            Name = "山田 太郎",
            NameKana = "ヤマダ タロウ",
            StaffNo = "E-0104",
            EmploymentType = "アルバイト",
            Status = StaffStatus.Active,
        });

        Assert.False(viewModel.IsNew);
        Assert.Equal("スタッフを編集", viewModel.Title);
        Assert.Equal("山田 太郎", viewModel.Name);
        Assert.Equal("ヤマダ タロウ", viewModel.NameKana);
        Assert.Equal("E-0104", viewModel.StaffNo);
        Assert.Equal("アルバイト", viewModel.EmploymentType);

        // 編集では発行済みのカードが変わらない。予告を出すと誤解を招く。
        Assert.Null(viewModel.IssueNote);
    }

    [Fact]
    public void 氏名が空なら保存できない()
    {
        var (viewModel, _) = Create();

        viewModel.OpenForAdd();
        Assert.False(viewModel.CanSave);

        viewModel.Name = "山田 太郎";
        Assert.True(viewModel.CanSave);

        viewModel.Name = "   ";
        Assert.False(viewModel.CanSave);
    }

    [Fact]
    public async Task 保存すると登録して完了を通知する()
    {
        var (viewModel, staff) = Create();

        viewModel.OpenForAdd();
        viewModel.Name = "山田 太郎";
        viewModel.StaffNo = "E-0104";

        var completed = false;
        viewModel.Completed += (_, _) => completed = true;

        await viewModel.SaveAsync();

        Assert.True(completed);
        Assert.Equal("山田 太郎", Assert.Single(staff.Added).Name);
    }

    [Fact]
    public async Task 編集では更新する()
    {
        var (viewModel, staff) = Create();
        var id = Guid.CreateVersion7();

        viewModel.OpenForEdit(new Staff
        {
            Id = id,
            StoreId = Guid.CreateVersion7(),
            Name = "山田 太郎",
            Status = StaffStatus.Active,
        });

        viewModel.Name = "山田 太朗";

        await viewModel.SaveAsync();

        Assert.Empty(staff.Added);
        Assert.Equal(id, Assert.Single(staff.Updated).StaffId);
    }

    [Fact]
    public async Task 社員番号が重複していたら保存しない()
    {
        var (viewModel, staff) = Create();
        staff.Error = "この社員番号はすでに使われています。";

        viewModel.OpenForAdd();
        viewModel.Name = "高橋 美咲";
        viewModel.StaffNo = "E-0104";

        var completed = false;
        viewModel.Completed += (_, _) => completed = true;

        await viewModel.SaveAsync();

        // 保存してからでは直せない。先に見る。
        Assert.Equal("この社員番号はすでに使われています。", viewModel.ErrorMessage);
        Assert.Empty(staff.Added);
        Assert.False(completed);
    }

    [Fact]
    public async Task 保存に失敗したら黙って閉じない()
    {
        var (viewModel, staff) = Create();
        staff.Throw = true;

        viewModel.OpenForAdd();
        viewModel.Name = "山田 太郎";

        var completed = false;
        viewModel.Completed += (_, _) => completed = true;

        await viewModel.SaveAsync();

        Assert.NotNull(viewModel.ErrorMessage);
        Assert.False(completed);
    }

    [Fact]
    public void 開き直すと前の入力が残らない()
    {
        var (viewModel, _) = Create();

        viewModel.OpenForAdd();
        viewModel.Name = "山田 太郎";
        viewModel.StaffNo = "E-0104";

        viewModel.OpenForAdd();

        // 残ると、前の人の情報で別人を登録してしまう。
        Assert.Equal(string.Empty, viewModel.Name);
        Assert.Equal(string.Empty, viewModel.StaffNo);
    }

    // ---- 補助 ----

    private static (StaffEditViewModel ViewModel, StubStaffService Staff) Create()
    {
        var staff = new StubStaffService();

        return (new StaffEditViewModel(staff), staff);
    }

    private sealed class StubStaffService : IStaffService
    {
        public string? Error { get; set; }

        public bool Throw { get; set; }

        public List<StaffDraft> Added { get; } = [];

        public List<StaffDraft> Updated { get; } = [];

        public Task<IReadOnlyList<StaffListItem>> ListAsync(
            StaffFilter filter = StaffFilter.All,
            string? search = null,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<StaffListItem>>([]);

        public Task<string?> ValidateAsync(StaffDraft draft, CancellationToken ct = default)
            => Task.FromResult(Error);

        public Task<Staff> AddAsync(StaffDraft draft, CancellationToken ct = default)
        {
            if (Throw)
            {
                throw new InvalidOperationException("保存に失敗しました。");
            }

            Added.Add(draft);

            return Task.FromResult(New(draft));
        }

        public Task<Staff> UpdateAsync(StaffDraft draft, CancellationToken ct = default)
        {
            if (Throw)
            {
                throw new InvalidOperationException("保存に失敗しました。");
            }

            Updated.Add(draft);

            return Task.FromResult(New(draft));
        }

        public Task<Staff> ChangeStatusAsync(
            Guid staffId, StaffStatus status, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<StaffQrToken> ReissueQrAsync(Guid staffId, CancellationToken ct = default)
            => throw new NotSupportedException();

        private static Staff New(StaffDraft draft) => new()
        {
            Id = draft.StaffId ?? Guid.CreateVersion7(),
            StoreId = Guid.CreateVersion7(),
            Name = draft.Name,
            Status = StaffStatus.Active,
        };
    }
}
