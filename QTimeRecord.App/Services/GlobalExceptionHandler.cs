using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using QTimeRecord.Core.Services;

namespace QTimeRecord.App.Services;

/// <summary>
/// 捕まえきれなかった例外の最後の受け皿。
///
/// <b>キオスク端末は営業時間中に落ちてはいけない。</b>
/// UI スレッドの例外は握りつぶさず記録したうえで、可能なら動作を継続させる。
/// 落ちてしまうと、その時点から誰も打刻できなくなる。
/// </summary>
public sealed class GlobalExceptionHandler(
    ILogger<GlobalExceptionHandler> logger,
    IUserErrorPresenter presenter,
    IDialogService dialogs)
{
    public void Attach(Application application)
    {
        application.DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        logger.LogError(e.Exception, "UI スレッドで未処理の例外が発生しました。");

        var error = presenter.Describe(e.Exception);

        try
        {
            dialogs.ShowError(error.Title, error.Message);
        }
        catch (Exception dialogFailure)
        {
            // ダイアログ自体が出せない状況（描画不能など）。ここで再帰させない。
            logger.LogError(dialogFailure, "エラーダイアログを表示できませんでした。");
        }

        // 継続する。打刻できない状態にするより、記録を残して動かし続けるほうが被害が小さい。
        e.Handled = true;
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        // ここへ来た時点でプロセスは基本的に終了する。落ちた理由だけは必ず残す。
        if (e.ExceptionObject is Exception exception)
        {
            logger.LogCritical(exception, "未処理の例外によりアプリケーションが終了します。");
        }
        else
        {
            logger.LogCritical("未処理の例外によりアプリケーションが終了します: {Object}", e.ExceptionObject);
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        logger.LogError(e.Exception, "監視されていないタスクで例外が発生しました。");

        // 観測済みにしておかないと、GC のタイミングでプロセスが落ちうる。
        e.SetObserved();
    }
}
