using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using QTimeRecord.Core.Infrastructure;

namespace QTimeRecord.Core.Services;

/// <summary>利用者に見せる内容。</summary>
public sealed record UserFacingError(string Title, string Message);

public interface IUserErrorPresenter
{
    UserFacingError Describe(Exception exception);
}

/// <summary>
/// 例外を、利用者に見せる短文へ変換する。
///
/// <b>画面には対処が分かる言葉だけを出し、原因の詳細はログに残す。</b>
/// スタックトレースや SQL を打刻画面に出しても、店舗スタッフには何もできない。
///
/// 業務上の分岐（未登録QR・休職スタッフなど）は例外ではなく戻り値で表現するため、
/// ここでは扱わない（→ plan.md 9-1）。ここに来るのは想定外の失敗だけ。
/// </summary>
public sealed class UserErrorPresenter(AppPaths paths) : IUserErrorPresenter
{
    public UserFacingError Describe(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception switch
        {
            // SqliteException も DbException を継承している
            DbUpdateException or DbException => new UserFacingError(
                "データベースエラー",
                "データを保存できませんでした。管理者へ連絡してください。"),

            UnauthorizedAccessException => new UserFacingError(
                "アクセスできません",
                "ファイルまたは機器にアクセスできませんでした。ほかのアプリが使用していないか確認してください。"),

            IOException => new UserFacingError(
                "ファイルエラー",
                "ファイルを読み書きできませんでした。保存先とディスクの空き容量を確認してください。"),

            OperationCanceledException => new UserFacingError(
                "処理を中断しました",
                "処理が中断されました。もう一度お試しください。"),

            // 原因が分からない失敗。調査できるようログの場所だけ添える
            // （→ plan.md 15-1）。内容は画面に出さない。
            _ => new UserFacingError(
                "エラーが発生しました",
                "予期しないエラーが発生しました。管理者へ連絡してください。"
                + $"{Environment.NewLine}ログ: {paths.LogDirectory}"),
        };
    }
}
