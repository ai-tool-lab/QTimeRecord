using System.Globalization;
using Microsoft.Extensions.Logging;
using QTimeRecord.Core.Data.Repositories;
using QTimeRecord.Core.Domain;
using QTimeRecord.Core.Infrastructure;

namespace QTimeRecord.Core.Services;

public interface ICsvExportService
{
    /// <summary>画面の条件そのままで CSV を作る。</summary>
    Task<byte[]> ExportAsync(AttendanceQuery query, CancellationToken ct = default);

    /// <summary>保存時に提案するファイル名。</summary>
    Task<string> SuggestFileNameAsync(AttendanceQuery query, CancellationToken ct = default);
}

/// <summary>
/// 勤務状況の CSV 出力。
///
/// <b>出力範囲は画面の表示と一致させる</b>（→ plan.md 14-1）。
/// 別の条件で出すと、画面で確認した内容と給与へ渡す内容が食い違う。
/// 集計そのものは <see cref="AttendanceAggregator"/> を通し、ここでは書式だけを扱う。
/// </summary>
public sealed class CsvExportService(
    IStoreRepository stores,
    IAttendanceQueryService attendance,
    ILogger<CsvExportService> logger) : ICsvExportService
{
    private static readonly CultureInfo Japanese = new("ja-JP");

    /// <summary>列名（→ plan.md 14-2）。</summary>
    public static readonly string[] Headers =
    [
        "店舗ID",
        "店舗名",
        "スタッフID",
        "社員番号",
        "スタッフ名",
        "区分",
        "日付",
        "曜日",
        "出勤",
        "退勤",
        "中抜け開始",
        "中抜け終了",
        "中抜け回数",
        "中抜け合計(分)",
        "実労働時間(分)",
        "打刻区分",
        "備考",
    ];

    public async Task<byte[]> ExportAsync(AttendanceQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var store = await stores.GetAsync(ct);
        var rows = await attendance.GetAsync(query, ct);

        var lines = new List<IEnumerable<string?>> { Headers };

        lines.AddRange(rows.Select(row => ToFields(store, row)));

        logger.LogInformation(
            "CSV を出力しました: {Year}/{Month} {Count}行", query.Year, query.Month, rows.Count);

        // 0件でもヘッダーだけの CSV を返す。空ファイルだと出力に失敗したのか
        // 対象が無かったのか区別できない。
        return CsvWriter.ToBytes(CsvWriter.Build(lines));
    }

    public async Task<string> SuggestFileNameAsync(
        AttendanceQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var store = await stores.GetAsync(ct);

        return BuildFileName(store?.StoreCode, query.Year, query.Month);
    }

    /// <summary>
    /// 「QTimeRecord_勤務状況_101_202609.csv」。
    /// 店舗コードが未設定なら、その部分を省く（空の区切りを残さない）。
    /// </summary>
    public static string BuildFileName(string? storeCode, int year, int month)
    {
        var period = $"{year:D4}{month:D2}";

        return string.IsNullOrWhiteSpace(storeCode)
            ? $"QTimeRecord_勤務状況_{period}.csv"
            : $"QTimeRecord_勤務状況_{storeCode.Trim()}_{period}.csv";
    }

    /// <summary>
    /// 打刻区分。手が入った行を給与担当が見分けられるようにする。
    /// 手入力と手修正が混ざる日は、より強い「手入力」を出す。
    /// </summary>
    public static string DescribeEntry(DailyAttendance summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        return summary switch
        {
            { HasManualAdd: true } => "手入力",
            { HasManualEdit: true } => "手修正",
            _ => "通常",
        };
    }

    private static IEnumerable<string?> ToFields(Store? store, AttendanceRow row)
    {
        var summary = row.Summary;

        return
        [
            store?.Id.ToString(),
            store?.StoreName,
            summary.StaffId.ToString(),
            row.StaffNo,
            row.StaffName,
            row.EmploymentType,
            summary.WorkDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Japanese.DateTimeFormat.GetShortestDayName(summary.WorkDate.DayOfWeek),
            Time(summary.ClockInAt),
            Time(summary.ClockOutAt),
            Time(summary.BreakStartAt),
            Time(summary.BreakEndAt),
            summary.BreakCount.ToString(CultureInfo.InvariantCulture),
            summary.BreakMinutes.ToString(CultureInfo.InvariantCulture),

            // 実労働は分の整数。小数だと Excel 上で誤差と誤解され、
            // 時分形式だと Excel が時刻型と解釈して集計を誤る（→ plan.md 14-2）。
            summary.WorkedMinutes?.ToString(CultureInfo.InvariantCulture),

            DescribeEntry(summary),
            summary.Note,
        ];
    }

    /// <summary>
    /// 時刻は分まで。秒まで出すと給与計算側で丸めが要る。
    /// 未打刻は空欄にする。「00:00」にすると0時の打刻と区別できない。
    /// </summary>
    private static string? Time(DateTime? value)
        => value?.ToString("HH:mm", CultureInfo.InvariantCulture);
}
