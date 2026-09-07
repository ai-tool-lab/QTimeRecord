using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QTimeRecord.Core.Domain;
using QTimeRecord.Core.Services;

namespace QTimeRecord.App.ViewModels;

/// <summary>
/// スタッフの登録・編集ダイアログ。
///
/// 在籍状態はここでは扱わない。状態の変更は QR の失効を伴うため、
/// 一覧側で確認を取ってから行う（→ plan.md Q15）。
/// </summary>
public sealed partial class StaffEditViewModel : ObservableObject
{
    /// <summary>区分の候補（→ plan.md Q6）。</summary>
    public static readonly string[] EmploymentTypes = ["社員", "アルバイト", "パート"];

    private readonly IStaffService _staff;

    private Guid? _staffId;

    public StaffEditViewModel(IStaffService staff)
    {
        _staff = staff;

        SaveCommand = new AsyncRelayCommand(SaveAsync, () => CanSave);

        foreach (var type in EmploymentTypes)
        {
            EmploymentTypeOptions.Add(type);
        }
    }

    /// <summary>保存が完了した。ダイアログを閉じて名簿を読み直す。</summary>
    public event EventHandler? Completed;

    public ObservableCollection<string> EmploymentTypeOptions { get; } = [];

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _nameKana = string.Empty;

    [ObservableProperty]
    private string _staffNo = string.Empty;

    [ObservableProperty]
    private string? _employmentType;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isBusy;

    public bool IsNew => _staffId is null;

    public string Title => IsNew ? "スタッフを登録" : "スタッフを編集";

    /// <summary>登録時は QR も同時に発行される。その旨を画面に出す。</summary>
    public string? IssueNote => IsNew
        ? "登録すると同時にQRコードを発行します。カードを印刷して本人へ渡してください。"
        : null;

    public bool CanSave => !IsBusy && !string.IsNullOrWhiteSpace(Name);

    public IRelayCommand SaveCommand { get; }

    /// <summary>新規登録として開く。</summary>
    public void OpenForAdd()
    {
        _staffId = null;

        Name = string.Empty;
        NameKana = string.Empty;
        StaffNo = string.Empty;
        EmploymentType = null;
        ErrorMessage = null;

        NotifyModeChanged();
    }

    /// <summary>既存スタッフの編集として開く。</summary>
    public void OpenForEdit(Staff staff)
    {
        ArgumentNullException.ThrowIfNull(staff);

        _staffId = staff.Id;

        // 候補に無い区分（過去の登録や移行データ）は選択肢へ足す。
        // 足さないと選択が外れ、保存した瞬間に区分が消える。
        if (!string.IsNullOrWhiteSpace(staff.EmploymentType)
            && !EmploymentTypeOptions.Contains(staff.EmploymentType))
        {
            EmploymentTypeOptions.Add(staff.EmploymentType);
        }

        Name = staff.Name;
        NameKana = staff.NameKana ?? string.Empty;
        StaffNo = staff.StaffNo ?? string.Empty;
        EmploymentType = staff.EmploymentType;
        ErrorMessage = null;

        NotifyModeChanged();
    }

    /// <summary>保存する。テストから直接呼べるよう公開する。</summary>
    public async Task SaveAsync()
    {
        if (!CanSave)
        {
            return;
        }

        var draft = new StaffDraft
        {
            StaffId = _staffId,
            Name = Name,
            NameKana = NameKana,
            StaffNo = StaffNo,
            EmploymentType = EmploymentType,
        };

        IsBusy = true;
        ErrorMessage = null;

        try
        {
            // 社員番号の重複は保存してからでは直せない。先に見る。
            if (await _staff.ValidateAsync(draft) is { } error)
            {
                ErrorMessage = error;
                return;
            }

            if (IsNew)
            {
                await _staff.AddAsync(draft);
            }
            else
            {
                await _staff.UpdateAsync(draft);
            }

            Completed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            // 保存できなかったことを黙って閉じない。
            ErrorMessage = $"保存できませんでした。{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void NotifyModeChanged()
    {
        OnPropertyChanged(nameof(IsNew));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(IssueNote));
    }

    partial void OnNameChanged(string value) => NotifyCanSaveChanged();

    partial void OnIsBusyChanged(bool value) => NotifyCanSaveChanged();

    private void NotifyCanSaveChanged()
    {
        OnPropertyChanged(nameof(CanSave));
        SaveCommand.NotifyCanExecuteChanged();
    }
}
