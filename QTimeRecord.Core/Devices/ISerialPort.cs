using System.IO.Ports;

namespace QTimeRecord.Core.Devices;

/// <summary>
/// シリアルポート。実機を差し替えられるようにするための最小の窓口。
///
/// <see cref="System.IO.Ports.SerialPort"/> を直接使うと、実機なしでは
/// 受信処理も接続断の扱いもテストできない。
/// </summary>
public interface ISerialPort : IDisposable
{
    bool IsOpen { get; }

    string PortName { get; }

    /// <summary>受信したバイト列。</summary>
    event EventHandler<byte[]>? DataReceived;

    /// <summary>受信中のエラー。接続断の検知に使う。</summary>
    event EventHandler<Exception>? ErrorReceived;

    void Open(SerialPortSettings settings);

    void Close();
}

/// <summary>接続のたびにポートを作る。使い回すと閉じたポートの再オープンで不安定になる。</summary>
public interface ISerialPortFactory
{
    ISerialPort Create();
}

/// <summary>利用可能な COM ポートの一覧。</summary>
public interface ISerialPortEnumerator
{
    IReadOnlyList<string> GetPortNames();
}

public sealed class SystemSerialPortFactory : ISerialPortFactory
{
    public ISerialPort Create() => new SystemSerialPort();
}

public sealed class SystemSerialPortEnumerator : ISerialPortEnumerator
{
    public IReadOnlyList<string> GetPortNames() => SerialPort.GetPortNames();
}

/// <summary><see cref="System.IO.Ports.SerialPort"/> の薄いラッパー。</summary>
public sealed class SystemSerialPort : ISerialPort
{
    private SerialPort? _port;

    public bool IsOpen => _port?.IsOpen ?? false;

    public string PortName => _port?.PortName ?? string.Empty;

    public event EventHandler<byte[]>? DataReceived;

    public event EventHandler<Exception>? ErrorReceived;

    public void Open(SerialPortSettings settings)
    {
        if (!settings.HasPort)
        {
            throw new InvalidOperationException("COM ポートが設定されていません。");
        }

        Close();

        var port = new SerialPort(
            settings.PortName, settings.BaudRate, settings.Parity, settings.DataBits, settings.StopBits)
        {
            // 読み取り待ちでスレッドを止めないための保険。
            ReadTimeout = 500,

            // 機種によっては DTR を上げないと送信を始めない。
            DtrEnable = true,
        };

        port.DataReceived += OnDataReceived;
        port.ErrorReceived += OnErrorReceived;

        port.Open();

        _port = port;
    }

    public void Close()
    {
        if (_port is null)
        {
            return;
        }

        _port.DataReceived -= OnDataReceived;
        _port.ErrorReceived -= OnErrorReceived;

        try
        {
            if (_port.IsOpen)
            {
                _port.Close();
            }
        }
        catch (IOException)
        {
            // USB を抜かれた直後の Close は失敗しうる。閉じられないこと自体は問題にしない。
        }

        _port.Dispose();
        _port = null;
    }

    public void Dispose() => Close();

    private void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        var port = _port;
        if (port is null || !port.IsOpen)
        {
            return;
        }

        try
        {
            var available = port.BytesToRead;
            if (available <= 0)
            {
                return;
            }

            var buffer = new byte[available];
            var read = port.Read(buffer, 0, available);

            if (read > 0)
            {
                DataReceived?.Invoke(this, read == buffer.Length ? buffer : buffer[..read]);
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException
                                       or UnauthorizedAccessException)
        {
            // 抜線の瞬間はここへ来る。上位で接続断として扱う。
            ErrorReceived?.Invoke(this, ex);
        }
    }

    private void OnErrorReceived(object sender, SerialErrorReceivedEventArgs e)
        => ErrorReceived?.Invoke(this, new IOException($"シリアル受信エラー: {e.EventType}"));
}
