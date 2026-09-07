using System.Security.Cryptography;
using System.Text;

namespace QTimeRecord.Core.Infrastructure;

/// <summary>
/// ログに出してよい形へ変換する。
///
/// ログファイルは平文で端末に残り、障害調査のために外へ持ち出されることもある。
/// <b>氏名は書かない（staff_id で記録する）。QR トークンは平文で残さない。</b>
/// </summary>
public static class LogSafe
{
    /// <summary>
    /// QR トークンの指紋。SHA-256 の先頭8文字だけを使う。
    ///
    /// 「同じQRが繰り返し読まれている」ことは追えるが、この値から元のトークンは復元できない。
    /// ログが漏れても、そのQRで打刻はできない。
    /// </summary>
    public static string TokenFingerprint(string? token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return "(空)";
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));

        return Convert.ToHexStringLower(hash)[..8];
    }
}
