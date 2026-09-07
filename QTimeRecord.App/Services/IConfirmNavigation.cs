namespace QTimeRecord.App.Services;

/// <summary>
/// 画面を離れてよいか確かめられる画面。
///
/// 未保存の変更があるまま別のタブへ移ると、入力が黙って消える。
/// タブを切り替える側（<c>AdminShellViewModel</c>）がこれを見て止める。
/// </summary>
public interface IConfirmNavigation
{
    /// <summary>離れてよければ true。必要なら確認ダイアログを出す。</summary>
    bool CanLeave();
}
