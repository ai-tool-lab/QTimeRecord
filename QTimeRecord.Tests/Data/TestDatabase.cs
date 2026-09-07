using System.IO;
using Microsoft.EntityFrameworkCore;
using QTimeRecord.Core.Data;

namespace QTimeRecord.Tests.Data;

/// <summary>
/// テスト用の使い捨て SQLite。
///
/// インメモリではなく実ファイルを使う。WAL・外部キー・PRAGMA の挙動を含めて
/// 本番と同じ経路で確認したいため（インメモリだとここが検証できない）。
/// </summary>
public sealed class TestDatabase : IDisposable
{
    private readonly string _directory;
    private readonly DbContextOptions<QTimeRecordDbContext> _options;

    public TestDatabase()
    {
        _directory = Path.Combine(Path.GetTempPath(), "qtimerecord-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);

        DatabasePath = Path.Combine(_directory, "qtimerecord.db");

        _options = new DbContextOptionsBuilder<QTimeRecordDbContext>()
            .UseSqlite(DbContextRegistration.BuildConnectionString(DatabasePath))
            .AddInterceptors(new SqlitePragmaInterceptor())
            .Options;

        Context = new QTimeRecordDbContext(_options);
        Factory = new TestDbContextFactory(_options);
    }

    public string DatabasePath { get; }

    /// <summary>直接検証したいとき用のコンテキスト。</summary>
    public QTimeRecordDbContext Context { get; }

    /// <summary>Repository へ渡すファクトリ。本番と同じく操作ごとに短命なコンテキストを作る。</summary>
    public IDbContextFactory<QTimeRecordDbContext> Factory { get; }

    /// <summary>マイグレーションを適用する。</summary>
    public void Migrate() => Context.Database.Migrate();

    /// <summary>同じ DB を見る別のコンテキストを作る（再読み込みの確認用）。</summary>
    public QTimeRecordDbContext CreateSeparateContext() => new(_options);

    public void Dispose()
    {
        Context.Dispose();

        // WAL を使うため -wal / -shm も残る。ディレクトリごと消す。
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // 後始末の失敗でテストを落とさない（一時ディレクトリなので実害がない）
        }
    }

    private sealed class TestDbContextFactory(DbContextOptions<QTimeRecordDbContext> options)
        : IDbContextFactory<QTimeRecordDbContext>
    {
        public QTimeRecordDbContext CreateDbContext() => new(options);
    }
}
