using System.Text;
using QTimeRecord.Core.Infrastructure;

namespace QTimeRecord.Tests.Infrastructure;

public sealed class CsvWriterTests
{
    [Theory]
    [InlineData("山田 太郎", "山田 太郎")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void 特別な文字が無ければ囲まない(string? value, string expected)
    {
        // 常に囲むと、取り込む側で引用符が値の一部として残ることがある。
        Assert.Equal(expected, CsvWriter.Escape(value));
    }

    [Fact]
    public void カンマを含む値は囲む()
    {
        Assert.Equal("\"山田, 太郎\"", CsvWriter.Escape("山田, 太郎"));
    }

    [Fact]
    public void 引用符は二重にして囲む()
    {
        // RFC4180: " は "" にする。
        Assert.Equal("\"山田 \"\"太郎\"\"\"", CsvWriter.Escape("山田 \"太郎\""));
    }

    [Theory]
    [InlineData("1行目\n2行目")]
    [InlineData("1行目\r\n2行目")]
    [InlineData("1行目\r2行目")]
    public void 改行を含む値は囲む(string value)
    {
        var escaped = CsvWriter.Escape(value);

        // 囲まないと、1件の値が2行に割れて列がずれる。
        Assert.StartsWith("\"", escaped, StringComparison.Ordinal);
        Assert.EndsWith("\"", escaped, StringComparison.Ordinal);
    }

    [Fact]
    public void 行はカンマでつなぐ()
    {
        Assert.Equal("A,B,C", CsvWriter.Line(["A", "B", "C"]));
    }

    [Fact]
    public void 空の値は空欄として並ぶ()
    {
        // 列がずれないよう、値が無くても位置は残す。
        Assert.Equal("A,,C", CsvWriter.Line(["A", null, "C"]));
    }

    [Fact]
    public void 行末はCRLFにする()
    {
        var content = CsvWriter.Build([["A", "B"], ["C", "D"]]);

        Assert.Equal("A,B\r\nC,D\r\n", content);
    }

    [Fact]
    public void BOMを先頭に付ける()
    {
        var bytes = CsvWriter.ToBytes("A,B\r\n");

        // BOM が無いと Excel が Shift_JIS と解釈し、氏名が化ける。
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
    }

    [Fact]
    public void 日本語がUTF8で書かれる()
    {
        var bytes = CsvWriter.ToBytes("山田 太郎\r\n");
        var text = Encoding.UTF8.GetString(bytes[3..]);

        Assert.Equal("山田 太郎\r\n", text);
    }

    [Fact]
    public void 囲んだ値も読み直せる形になる()
    {
        // 実際に読み戻して、列がずれないことを確かめる。
        var line = CsvWriter.Line(["山田, 太郎", "備考\"あり\"", "通常"]);

        Assert.Equal(
            ["山田, 太郎", "備考\"あり\"", "通常"],
            CsvReader.Parse(line + CsvWriter.NewLine)[0]);
    }
}
