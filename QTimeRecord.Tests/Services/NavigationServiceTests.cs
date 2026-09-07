using Microsoft.Extensions.DependencyInjection;
using QTimeRecord.App.Services;

namespace QTimeRecord.Tests.Services;

public sealed class NavigationServiceTests
{
    private sealed class FirstViewModel;

    private sealed class SecondViewModel;

    private sealed class DisposableViewModel : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }

    private static NavigationService CreateService()
    {
        var services = new ServiceCollection();
        services.AddTransient<FirstViewModel>();
        services.AddTransient<SecondViewModel>();

        return new NavigationService(services.BuildServiceProvider());
    }

    [Fact]
    public void DIから解決して遷移する()
    {
        var navigation = CreateService();

        navigation.NavigateTo<FirstViewModel>();

        Assert.IsType<FirstViewModel>(navigation.Current);
    }

    [Fact]
    public void 遷移すると通知される()
    {
        var navigation = CreateService();
        object? notified = null;

        navigation.Navigated += (_, viewModel) => notified = viewModel;
        navigation.NavigateTo<SecondViewModel>();

        Assert.IsType<SecondViewModel>(notified);
    }

    [Fact]
    public void 前の画面が破棄される()
    {
        var navigation = CreateService();
        var first = new DisposableViewModel();

        navigation.NavigateTo(first);
        navigation.NavigateTo<FirstViewModel>();

        // タイマーやシリアル購読を残したまま画面を切り替えると、
        // 裏で動き続けて誤打刻や多重処理の原因になる。
        Assert.True(first.Disposed);
    }

    [Fact]
    public void 同じ画面への遷移では破棄しない()
    {
        var navigation = CreateService();
        var viewModel = new DisposableViewModel();

        navigation.NavigateTo(viewModel);
        navigation.NavigateTo(viewModel);

        Assert.False(viewModel.Disposed);
    }
}
