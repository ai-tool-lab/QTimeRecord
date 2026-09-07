using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using QTimeRecord.App.Services;
using QTimeRecord.Core.Domain;
using QTimeRecord.Core.Infrastructure;
using QTimeRecord.Core.Services;

namespace QTimeRecord.App.ViewModels;

/// <summary>名簿の絞り込みの選択肢。</summary>
/// <param name="Filter">絞り込みの種類。</param>
/// <param name="Label">ボタンの文言。</param>
public sealed record StaffFilterOption(StaffFilter Filter, string Label);

/// <summary>在籍状態の選択肢。</summary>
/// <param name="Status">状態。</param>
/// <param name="Label">表示名。</param>
public sealed record StaffStatusOption(StaffStatus Status, string Label);

/// <summary>名簿の1行の表示。</summary>
public sealed record StaffListRow(StaffListItem Source)
{
    private static readonly CultureInfo Japanese = new("ja-JP");

    public Staff Staff => Source.Staff;

    public string Name => Source.Staff.Name;

    public string? NameKana => Source.Staff.NameKana;

    public string? StaffNo => Source.Staff.StaffNo;

    public string? EmploymentType => Source.Staff.EmploymentType;

    public string StatusText => StaffViewModel.Describe(Source.Staff.Status);

    public bool IsRetired => Source.Staff.Status == StaffStatus.Retired;

    public bool IsOnLeave => Source.Staff.Status == StaffStatus.OnLeave;

    /// <summary>「発行済 (2026/09/01)」または「QR無効化済」。</summary>
    public string QrText => Source.IssuedAt is { } issued
        ? $"発行済 ({issued.ToString("yyyy/MM/dd", Japanese)})"
        : "QR無効化済";

    public bool HasActiveQr => Source.HasActiveQr;
}

/// <summary>
/// スタッフ管理。左に名簿、右に選択中スタッフの詳細と QR カード。
/// </summary>
public sealed partial class StaffViewModel : ObservableObject
{
    private readonly IStaffService _staff;
    private readonly IStaffCardService _cards;
    private readonly IStaffEditor _editor;
    private readonly IDialogService _dialogs;
    private readonly IFileDialogService _files;
    private readonly AppPaths _paths;
    private readonly ILogger<StaffViewModel> _logger;

    public StaffViewModel(
        IStaffService staff,
        IStaffCardService cards,
        IStaffEditor editor,
        IDialogService dialogs,
        IFileDialogService files,
        AppPaths paths,
        ILogger<StaffViewModel> logger)
    {
        _staff = staff;
        _cards = cards;
        _editor = editor;
        _dialogs = dialogs;
        _files = files;
        _paths = paths;
        _logger = logger;

        _selectedFilter = Filters[0];

        AddCommand = new AsyncRelayCommand(AddAsync);
        EditCommand = new AsyncRelayCommand(EditAsync, () => SelectedStaff is not null);
        ChangeStatusCommand = new AsyncRelayCommand<StaffStatusOption?>(ChangeStatusAsync);
        ReissueQrCommand = new AsyncRelayCommand(ReissueQrAsync, () => SelectedStaff is not null);
        SaveCardCommand = new AsyncRelayCommand(SaveCardAsync, () => CardPng is not null);
    }

    public static IReadOnlyList<StaffFilterOption> Filters { get; } =
    [
        new(StaffFilter.All, "全員"),
        new(StaffFilter.FullTime, "社員"),
        new(StaffFilter.PartTime, "アルバイト・パート"),
        new(StaffFilter.Retired, "退職者"),
    ];

    public static IReadOnlyList<StaffStatusOption> StatusOptions { get; } =
    [
        new(StaffStatus.Active, "在籍中"),
        new(StaffStatus.OnLeave, "休職中"),
        new(StaffStatus.Retired, "退職済"),
    ];

    public ObservableCollection<StaffListRow> Rows { get; } = [];

    [ObservableProperty]
    private StaffListRow? _selectedStaff;

    [ObservableProperty]
    private StaffFilterOption _selectedFilter;

    [ObservableProperty]
    private string _search = string.Empty;

    /// <summary>選択中スタッフの QR カード画像。未発行なら null。</summary>
    [ObservableProperty]
    private byte[]? _cardPng;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    public bool HasSelection => SelectedStaff is not null;

    public bool IsEmpty => Rows.Count == 0 && !IsBusy;

    public string CountLabel => $"全 {Rows.Count} 名";

    /// <summary>選択中スタッフの在籍状態。ドロップダウンで変える。</summary>
    public StaffStatusOption? SelectedStatus => SelectedStaff is null
        ? null
        : StatusOptions.FirstOrDefault(o => o.Status == SelectedStaff.Staff.Status);

    public ICommand AddCommand { get; }

    public IRelayCommand EditCommand { get; }

    public ICommand ChangeStatusCommand { get; }

    public IRelayCommand ReissueQrCommand { get; }

    public IRelayCommand SaveCardCommand { get; }

    public static string Describe(StaffStatus status) => status switch
    {
        StaffStatus.Active => "在籍中",
        StaffStatus.OnLeave => "休職中",
        StaffStatus.Retired => "退職済",
        _ => "不明",
    };

    /// <summary>名簿を読み込む。</summary>
    public async Task LoadAsync(CancellationToken ct = default)
    {
        IsBusy = true;
        ErrorMessage = null;

        // 選択が消えると、操作の途中で右側だけ空になる。同じ人を選び直す。
        var previous = SelectedStaff?.Staff.Id;

        try
        {
            var items = await _staff.ListAsync(SelectedFilter.Filter, Search, ct);

            Rows.Clear();

            foreach (var item in items)
            {
                Rows.Add(new StaffListRow(item));
            }

            SelectedStaff = Rows.FirstOrDefault(r => r.Staff.Id == previous) ?? Rows.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "スタッフ名簿を読み込めませんでした。");
            ErrorMessage = "スタッフ名簿を読み込めませんでした。しばらくしてからやり直してください。";
        }
        finally
        {
            IsBusy = false;

            OnPropertyChanged(nameof(CountLabel));
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    private async Task AddAsync()
    {
        if (await _editor.AddAsync())
        {
            await LoadAsync();
        }
    }

    private async Task EditAsync()
    {
        if (SelectedStaff is { } row && await _editor.EditAsync(row.Staff))
        {
            await LoadAsync();
        }
    }

    /// <summary>在籍状態を変える。テストから直接呼べるよう公開する。</summary>
    public async Task ChangeStatusAsync(StaffStatusOption? option)
    {
        if (option is null || SelectedStaff is not { } row || row.Staff.Status == option.Status)
        {
            return;
        }

        // 退職にすると配布済みのカードが使えなくなる。戻すには再発行が要る。
        if (option.Status == StaffStatus.Retired && !_dialogs.Confirm(
            "退職済にしますか？",
            $"{row.Name} さんのQRコードを無効にします。"
            + $"{Environment.NewLine}復帰する場合はQRの再発行が必要です。",
            "退職済にする",
            "やめる"))
        {
            // 取り消したのに選択だけ進んでいると、変わったように見えてしまう。
            OnPropertyChanged(nameof(SelectedStatus));
            return;
        }

        await RunAsync(async () =>
        {
            await _staff.ChangeStatusAsync(row.Staff.Id, option.Status);
            await LoadAsync();
        });

        OnPropertyChanged(nameof(SelectedStatus));
    }

    private async Task ReissueQrAsync()
    {
        if (SelectedStaff is not { } row)
        {
            return;
        }

        // 再発行すると旧カードが使えなくなる。押し間違いは配布のやり直しになる。
        if (!_dialogs.Confirm(
            "QRコードを再発行しますか？",
            $"{row.Name} さんの現在のQRコードは使えなくなります。"
            + $"{Environment.NewLine}新しいカードを印刷して渡してください。",
            "再発行する",
            "やめる"))
        {
            return;
        }

        await RunAsync(async () =>
        {
            await _staff.ReissueQrAsync(row.Staff.Id);
            await LoadAsync();
        });
    }

    private async Task SaveCardAsync()
    {
        if (CardPng is not { } png || SelectedStaff is not { } row)
        {
            return;
        }

        var path = _files.AskSavePath(
            "QRカードの保存先",
            _cards.SuggestFileName(row.Staff),
            "PNG 画像 (*.png)|*.png",
            _paths.DefaultExportDirectory);

        if (path is null)
        {
            return;
        }

        try
        {
            await _cards.SaveAsync(png, path);
            _dialogs.ShowInfo("保存しました", "QRカードの画像を保存しました。");
        }
        catch (Exception ex)
        {
            // 保存できていないのに成功に見せると、印刷しようとして初めて気づく。
            _logger.LogError(ex, "QR カードを保存できませんでした。");
            _dialogs.ShowError("保存できませんでした", "保存先を確認してください。");
        }
    }

    /// <summary>選択中スタッフのカード画像を作り直す。</summary>
    private async Task RefreshCardAsync()
    {
        if (SelectedStaff is not { } row)
        {
            CardPng = null;
            return;
        }

        try
        {
            CardPng = (await _cards.RenderAsync(row.Staff))?.Png;
        }
        catch (Exception ex)
        {
            // 「未発行」と「生成に失敗した」は原因も対処も違う（→ plan.md 15-1）。
            _logger.LogError(ex, "QR カードを描画できませんでした: {StaffId}", row.Staff.Id);

            CardPng = null;
            ErrorMessage = "QRコードを生成できませんでした。";
        }
    }

    private async Task RunAsync(Func<Task> action)
    {
        IsBusy = true;
        ErrorMessage = null;

        try
        {
            await action();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "スタッフの操作に失敗しました。");
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnSelectedStaffChanged(StaffListRow? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectedStatus));

        EditCommand.NotifyCanExecuteChanged();
        ReissueQrCommand.NotifyCanExecuteChanged();

        _ = RefreshCardAsync();
    }

    partial void OnCardPngChanged(byte[]? value) => SaveCardCommand.NotifyCanExecuteChanged();

    partial void OnSelectedFilterChanged(StaffFilterOption value) => _ = LoadAsync();

    partial void OnSearchChanged(string value) => _ = LoadAsync();

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsEmpty));
}
