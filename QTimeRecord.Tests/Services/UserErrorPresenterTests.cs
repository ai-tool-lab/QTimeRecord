using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QTimeRecord.Core.Infrastructure;
using QTimeRecord.Core.Services;

namespace QTimeRecord.Tests.Services;

public sealed class UserErrorPresenterTests
{
    private readonly UserErrorPresenter _presenter = new(new AppPaths(new AppSettings()));

    [Fact]
    public void DBの失敗は保存できなかったことを伝える()
    {
        var error = _presenter.Describe(new DbUpdateException("制約違反"));

        Assert.Equal("データベースエラー", error.Title);
        Assert.Contains("保存できませんでした", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SQLiteの例外もDBの失敗として扱う()
    {
        var error = _presenter.Describe(new SqliteException("locked", 5));

        Assert.Equal("データベースエラー", error.Title);
    }

    [Fact]
    public void ファイルとアクセス権の失敗を区別する()
    {
        Assert.Equal("アクセスできません", _presenter.Describe(new UnauthorizedAccessException()).Title);
        Assert.Equal("ファイルエラー", _presenter.Describe(new IOException()).Title);
    }

    [Fact]
    public void 想定外の例外でも必ずメッセージを返す()
    {
        // 何が来ても画面に出す文言が無い、という状態を作らない。
        var error = _presenter.Describe(new InvalidProgramException("想定外"));

        Assert.False(string.IsNullOrWhiteSpace(error.Title));
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
    }

    [Fact]
    public void 内部情報を利用者に見せない()
    {
        var exception = new DbUpdateException(
            "SQLite Error 19: 'FOREIGN KEY constraint failed'. INSERT INTO time_records ...");

        var error = _presenter.Describe(exception);

        // SQL やスタックトレースを打刻画面に出しても、店舗スタッフには何もできない。
        Assert.DoesNotContain("INSERT", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SQLite", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void 予期しない例外にはログの場所を添える()
    {
        var error = _presenter.Describe(new InvalidOperationException("なにか"));

        // 原因が分からない失敗は、調査できる手がかりだけを残す（→ plan.md 15-1）。
        Assert.Contains("ログ:", error.Message, StringComparison.Ordinal);

        // 例外の内容そのものは画面に出さない。
        Assert.DoesNotContain("なにか", error.Message, StringComparison.Ordinal);
    }
}
