namespace QTimeRecord.Core.Infrastructure;

/// <summary>
/// アプリが使うファイルの置き場所。
///
/// <b>%LOCALAPPDATA% 配下に置く。</b>
/// Program Files 配下は書き込みに管理者権限が要り、標準ユーザーで運用する店舗PCでは
/// 打刻が保存できなくなる。複数ユーザーで同一データを共有する要件が出たら %ProgramData%
/// へ移すが、MVP は1端末1アカウント運用のため不要。
/// </summary>
public sealed class AppPaths(AppSettings settings)
{
    public string DatabasePath { get; } = settings.ResolvedDatabasePath;

    public string LogDirectory { get; } = settings.ResolvedLogDirectory;

    /// <summary>CSV 出力の初期表示フォルダ。</summary>
    public string DefaultExportDirectory { get; } =
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    /// <summary>必要なフォルダを作る。起動時に1度呼ぶ。</summary>
    public void EnsureCreated()
    {
        var databaseDirectory = Path.GetDirectoryName(DatabasePath);
        if (!string.IsNullOrEmpty(databaseDirectory))
        {
            Directory.CreateDirectory(databaseDirectory);
        }

        Directory.CreateDirectory(LogDirectory);
    }
}
