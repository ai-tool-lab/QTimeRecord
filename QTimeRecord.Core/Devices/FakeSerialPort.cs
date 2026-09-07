using System.Text;

namespace QTimeRecord.Core.Devices;

/// <summary>
/// 実機なしで受信処理を動かすためのポート。
///
/// テストのほか、リーダーが手元に無い状態での画面確認にも使える。
/// </summary>
public sealed class FakeSerialPort : ISerialPort
{
    public bool IsOpen { get; private set; }

    public string PortName { get; private set; } = string.Empty;

    /// <summary>Open を試みたときに投げる例外。接続失敗の再現に使う。</summary>
    public Exception? OpenFailure { get; set; }

    public event EventHandler<byte[]>? DataReceived;

    public event EventHandler<Exception>? ErrorReceived;

    public void Open(SerialPortSettings settings)
    {
        if (OpenFailure is not null)
        {
            throw OpenFailure;
        }

        PortName = settings.PortName;
        IsOpen = true;
    }

    public void Close() => IsOpen = false;

    public void Dispose() => Close();

    /// <summary>受信を再現する。</summary>
    public void Receive(string text) => Receive(Encoding.ASCII.GetBytes(text));

    public void Receive(byte[] data) => DataReceived?.Invoke(this, data);

    /// <summary>1文字ずつ届く状況を再現する。実機では分割して届くことがある。</summary>
    public void ReceiveInChunks(string text, int chunkSize = 1)
    {
        var bytes = Encoding.ASCII.GetBytes(text);

        for (var offset = 0; offset < bytes.Length; offset += chunkSize)
        {
            var length = Math.Min(chunkSize, bytes.Length - offset);
            Receive(bytes[offset..(offset + length)]);
        }
    }

    /// <summary>抜線などのエラーを再現する。</summary>
    public void RaiseError(Exception? exception = null)
    {
        IsOpen = false;
        ErrorReceived?.Invoke(this, exception ?? new IOException("接続が切れました。"));
    }
}
