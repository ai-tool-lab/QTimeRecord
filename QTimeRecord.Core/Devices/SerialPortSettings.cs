using System.IO.Ports;
using QTimeRecord.Core.Domain;

namespace QTimeRecord.Core.Devices;

/// <summary>
/// シリアルポートの接続設定。店舗設定に保存した値から作る。
/// </summary>
public sealed record SerialPortSettings
{
    public required string PortName { get; init; }

    public int BaudRate { get; init; } = 9600;

    public int DataBits { get; init; } = 8;

    public Parity Parity { get; init; } = Parity.None;

    public StopBits StopBits { get; init; } = StopBits.One;

    /// <summary>保存済みの設定から作る。列挙値が壊れていれば既定値に倒す。</summary>
    public static SerialPortSettings From(DeviceSettings settings)
        => new()
        {
            PortName = settings.ComPort ?? string.Empty,
            BaudRate = settings.BaudRate,
            DataBits = settings.DataBits,
            Parity = Enum.TryParse<Parity>(settings.Parity, ignoreCase: true, out var parity)
                ? parity
                : Parity.None,
            StopBits = Enum.TryParse<StopBits>(settings.StopBits, ignoreCase: true, out var stopBits)
                ? stopBits
                : StopBits.One,
        };

    /// <summary>ポートが指定されているか。未設定のまま接続を試みない。</summary>
    public bool HasPort => !string.IsNullOrWhiteSpace(PortName);

    public override string ToString()
        => $"{PortName} {BaudRate}bps {DataBits}{Parity.ToString()[0]}{StopBitsText}";

    private string StopBitsText => StopBits switch
    {
        StopBits.One => "1",
        StopBits.Two => "2",
        StopBits.OnePointFive => "1.5",
        _ => "?",
    };
}
