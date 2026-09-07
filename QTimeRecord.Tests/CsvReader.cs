using System.Text;

namespace QTimeRecord.Tests;

/// <summary>
/// 検証用の最小の CSV 読み取り。RFC4180 の囲みだけを解く。
///
/// 単純に改行で切ると、<b>値の中の改行で行が割れて</b>本来の不具合を見逃す。
/// 出力した CSV を「読み直せる形になっているか」で確かめるために置く。
/// </summary>
public static class CsvReader
{
    public static IReadOnlyList<IReadOnlyList<string>> Parse(string content)
    {
        var records = new List<IReadOnlyList<string>>();
        var fields = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        var hasContent = false;

        for (var i = 0; i < content.Length; i++)
        {
            var c = content[i];

            if (quoted)
            {
                if (c == '"' && i + 1 < content.Length && content[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    current.Append(c);
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    quoted = true;
                    hasContent = true;
                    break;

                case ',':
                    fields.Add(current.ToString());
                    current.Clear();
                    hasContent = true;
                    break;

                case '\r':
                    break;

                case '\n':
                    fields.Add(current.ToString());
                    current.Clear();

                    if (hasContent)
                    {
                        records.Add(fields);
                        fields = [];
                    }
                    else
                    {
                        fields.Clear();
                    }

                    hasContent = false;
                    break;

                default:
                    current.Append(c);
                    hasContent = true;
                    break;
            }
        }

        if (hasContent)
        {
            fields.Add(current.ToString());
            records.Add(fields);
        }

        return records;
    }
}
