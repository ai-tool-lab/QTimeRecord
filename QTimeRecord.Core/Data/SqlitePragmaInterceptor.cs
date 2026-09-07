using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace QTimeRecord.Core.Data;

/// <summary>
/// 接続を開くたびに SQLite の PRAGMA を設定する。
///
/// journal_mode は DB ファイルに永続する設定だが、
/// foreign_keys と synchronous は<b>接続ごと</b>に指定する必要がある。
/// 接続文字列では指定できないため、ここで実行する。
/// </summary>
public sealed class SqlitePragmaInterceptor : DbConnectionInterceptor
{
    public override void ConnectionOpened(
        DbConnection connection, ConnectionEndEventData eventData)
    {
        Apply(connection);
        base.ConnectionOpened(connection, eventData);
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        Apply(connection);
        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
    }

    private static void Apply(DbConnection connection)
    {
        using var command = connection.CreateCommand();

        // foreign_keys: 既定は OFF。スタッフを消すと打刻が孤児になるため必ず有効にする。
        // journal_mode=WAL: 読み書きの衝突を減らす。DB ファイルに永続する。
        // synchronous=FULL: 書き込みごとにディスクへ同期する。
        //   打刻は1日数十件で書き込み頻度が低く、性能上の不利がほぼない。
        //   一方で「電源断で直前の打刻が消える」ことは勤怠として許容できない。
        command.CommandText =
            """
            PRAGMA foreign_keys = ON;
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = FULL;
            """;

        command.ExecuteNonQuery();
    }
}
