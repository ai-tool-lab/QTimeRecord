using System.Text;

namespace QTimeRecord.Core.Infrastructure;

/// <summary>
/// CSV の組み立て。RFC4180 準拠。
///
/// ライブラリを入れずに自前で書く。列は固定で数も少なく、
/// 必要なのは「囲みとエスケープを間違えないこと」だけ（→ plan.md 3章）。
///
/// <b>Excel で開くことが前提</b>なので、UTF-8 BOM 付き・CRLF で出す。
/// BOM が無いと Excel が Shift_JIS と解釈して氏名が化ける。
/// </summary>
public static class CsvWriter
{
    /// <summary>改行は CRLF。Excel と給与ソフトの多くがこれを前提にしている。</summary>
    public const string NewLine = "\r\n";

    private const char Quote = '"';
    private const char Separator = ',';

    /// <summary>
    /// 1つの値を CSV の書式にする。
    ///
    /// 囲むのは<b>カンマ・引用符・改行を含むときだけ</b>。
    /// 常に囲むと、そのまま取り込む側で引用符が値の一部として残ることがある。
    /// </summary>
    public static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var needsQuotes = value.Contains(Separator, StringComparison.Ordinal)
            || value.Contains(Quote)
            || value.Contains('\r')
            || value.Contains('\n');

        if (!needsQuotes)
        {
            return value;
        }

        return string.Create(
            null,
            stackalloc char[0],
            $"{Quote}{value.Replace("\"", "\"\"", StringComparison.Ordinal)}{Quote}");
    }

    /// <summary>1行ぶんを組み立てる。行末の改行は含めない。</summary>
    public static string Line(IEnumerable<string?> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);

        return string.Join(Separator, fields.Select(Escape));
    }

    /// <summary>行の並びから CSV 本文を作る。</summary>
    public static string Build(IEnumerable<IEnumerable<string?>> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var builder = new StringBuilder();

        foreach (var row in rows)
        {
            builder.Append(Line(row)).Append(NewLine);
        }

        return builder.ToString();
    }

    /// <summary>ファイルに書くバイト列にする。BOM を必ず付ける。</summary>
    public static byte[] ToBytes(string content)
        => [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(content)];
}
