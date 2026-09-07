using Microsoft.Extensions.DependencyInjection;

namespace QTimeRecord.App.Services;

public interface INavigationService
{
    /// <summary>現在表示している画面の ViewModel。</summary>
    object? Current { get; }

    /// <summary>画面が切り替わったとき。シェルが購読して表示を差し替える。</summary>
    event EventHandler<object?>? Navigated;

    /// <summary>DI から ViewModel を解決して遷移する。</summary>
    void NavigateTo<TViewModel>() where TViewModel : class;

    /// <summary>すでに組み立てた ViewModel へ遷移する（引数を渡したい場合）。</summary>
    void NavigateTo(object viewModel);
}

/// <summary>
/// 画面遷移。
///
/// キオスク用途のためウィンドウは増やさず、シェルの表示内容を差し替える方式にする。
/// シェルを直接触らずイベントで通知するのは、
/// ShellViewModel → NavigationService → ShellViewModel の循環参照を避けるため。
/// </summary>
public sealed class NavigationService(IServiceProvider provider) : INavigationService
{
    public object? Current { get; private set; }

    public event EventHandler<object?>? Navigated;

    public void NavigateTo<TViewModel>() where TViewModel : class
        => NavigateTo(provider.GetRequiredService<TViewModel>());

    public void NavigateTo(object viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        // 前の画面が破棄可能なら片付ける。タイマーやシリアル購読を残さないため。
        if (Current is IDisposable disposable && !ReferenceEquals(Current, viewModel))
        {
            disposable.Dispose();
        }

        Current = viewModel;
        Navigated?.Invoke(this, viewModel);
    }
}
