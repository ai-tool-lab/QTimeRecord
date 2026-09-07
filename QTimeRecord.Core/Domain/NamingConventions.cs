using System.Text;

namespace QTimeRecord.Core.Domain;

/// <summary>
/// 名前の変換規則。DB の識別子と列挙型の保存値で共通に使う。
/// </summary>
public static class NamingConventions
{
    /// <summary>
    /// パスカルケースをスネークケースにする。
    ///
    /// <code>
    /// StaffQrTokens          → staff_qr_tokens
    /// IsOutsideBusinessHours → is_outside_business_hours
    /// PK_stores              → pk_stores
    /// </code>
    ///
    /// 連続する大文字を区切らないのは、EF が付ける <c>PK_</c> や <c>IX_</c> のような
    /// 略語が <c>p_k_</c> のように分解されるのを避けるため。
    /// </summary>
    public static string ToSnakeCase(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }

        var sb = new StringBuilder(name.Length + 8);

        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];

            if (i > 0 && char.IsUpper(c) && NeedsSeparator(name, i))
            {
                sb.Append('_');
            }

            sb.Append(char.ToLowerInvariant(c));
        }

        return sb.ToString();
    }

    private static bool NeedsSeparator(string name, int i)
    {
        if (name[i - 1] == '_')
        {
            return false;
        }

        // 小文字・数字のあとの大文字は語の切れ目（staffQr → staff_qr）
        if (!char.IsUpper(name[i - 1]))
        {
            return true;
        }

        // 大文字が続いたあとに小文字が来る場合も切れ目（QRCode → qr_code）
        return i + 1 < name.Length && char.IsLower(name[i + 1]);
    }
}
