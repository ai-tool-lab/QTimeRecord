using CommunityToolkit.Mvvm.ComponentModel;
using QTimeRecord.App.Services;

namespace QTimeRecord.App.ViewModels;

/// <summary>
/// メインウィンドウ（シェル）の状態。
///
/// キオスク用途のためウィンドウは増やさず、この <see cref="CurrentView"/> を
/// 差し替えて画面遷移する。差し替えを指示するのは <see cref="INavigationService"/>。
/// </summary>
public sealed partial class ShellViewModel : ObservableObject
{
    public ShellViewModel(INavigationService navigation)
    {
        navigation.Navigated += (_, viewModel) => CurrentView = viewModel;
        CurrentView = navigation.Current;
    }

    /// <summary>現在表示している画面の ViewModel。</summary>
    [ObservableProperty]
    private object? _currentView;
}
