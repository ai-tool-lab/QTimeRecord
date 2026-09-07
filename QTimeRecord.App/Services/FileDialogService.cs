using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace QTimeRecord.App.Services;

/// <summary>
/// OS のファイル操作。保存先の選択と、保存したファイルの場所を開くこと。
///
/// ViewModel から <c>SaveFileDialog</c> や <c>Process.Start</c> を直接呼ばないためのもの。
/// 直接呼ぶと、保存処理をテストするたびにダイアログやエクスプローラーが開く。
/// </summary>
public interface IFileDialogService
{
    /// <summary>保存先のパスを選ばせる。取り消したら null。</summary>
    string? AskSavePath(string title, string suggestedFileName, string filter, string? initialDirectory);

    /// <summary>保存したファイルをエクスプローラーで選択した状態で開く。</summary>
    void RevealInFolder(string path);
}

public sealed class FileDialogService : IFileDialogService
{
    public string? AskSavePath(
        string title, string suggestedFileName, string filter, string? initialDirectory)
    {
        var dialog = new SaveFileDialog
        {
            Title = title,
            FileName = suggestedFileName,
            Filter = filter,
            AddExtension = true,

            // 上書きは取り返しがつかない。既定で確認を出す。
            OverwritePrompt = true,
        };

        if (!string.IsNullOrWhiteSpace(initialDirectory))
        {
            dialog.InitialDirectory = initialDirectory;
        }

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public void RevealInFolder(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        // /select を付けると、フォルダを開いたうえでそのファイルを選んだ状態になる。
        // 「どこに保存されたか分からない」という問い合わせがいちばん多い。
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{path}\"",
            UseShellExecute = true,
        });
    }
}
