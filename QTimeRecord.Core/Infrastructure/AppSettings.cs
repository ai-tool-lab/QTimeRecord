namespace QTimeRecord.Core.Infrastructure;

/// <summary>
/// appsettings.json から読む設定。
///
/// ここに置くのは <b>DBを開く前に必要なものだけ</b>。
/// 店舗情報・お知らせ・シリアルポート設定は SQLite 側で持つ（→ plan.md 13-1）。
/// 両方に設定が散ると「設定だけ古い」状態が生まれるため、この線引きを崩さないこと。
/// </summary>
public sealed class AppSettings
{
    public const string SectionName = "QTimeRecord";

    /// <summary>SQLite ファイルのパス。<c>%LOCALAPPDATA%</c> などの環境変数を展開して使う。</summary>
    public string DatabasePath { get; init; } = @"%LOCALAPPDATA%\QTimeRecord\qtimerecord.db";

    /// <summary>ログの出力先ディレクトリ。</summary>
    public string LogDirectory { get; init; } = @"%LOCALAPPDATA%\QTimeRecord\logs";

    /// <summary>ログの最小レベル。既定は Information、調査時のみ Debug にする。</summary>
    public string LogLevel { get; init; } = "Information";

    /// <summary>環境変数を展開した DB ファイルの絶対パス。</summary>
    public string ResolvedDatabasePath => Environment.ExpandEnvironmentVariables(DatabasePath);

    /// <summary>環境変数を展開したログ出力先の絶対パス。</summary>
    public string ResolvedLogDirectory => Environment.ExpandEnvironmentVariables(LogDirectory);
}
