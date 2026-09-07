using System.Text;

namespace QTimeRecord.Core.Devices;

/// <summary>
/// シリアルから届いたバイト列を、1件ずつのスキャン結果へ切り分ける。
///
/// 実機は1回の読み取りを<b>複数回に分けて</b>送ってくることがあり、
/// 逆に2件が1回にまとまって届くこともある。終端文字も機種によって CR / LF / CRLF と違う。
/// ここで吸収し、上位には「トークン文字列が1件」だけを渡す。
/// </summary>
public sealed class ScanBuffer(TimeSpan? idleTimeout = null, int? maxLength = null)
{
    /// <summary>
    /// 終端文字を送らない機種のための保険。
    /// これだけ無受信が続いたら、溜まっている分を1件として確定する。
    /// </summary>
    public static readonly TimeSpan DefaultIdleTimeout = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// 1件の上限。これを超えたら異常なデータとみなして捨てる。
    /// ノイズが乗り続けたときにメモリを食い潰さないため。
    /// </summary>
    public const int DefaultMaxLength = 512;

    private readonly TimeSpan _idleTimeout = idleTimeout ?? DefaultIdleTimeout;
    private readonly int _maxLength = maxLength ?? DefaultMaxLength;
    private readonly StringBuilder _pending = new();

    private DateTime _lastReceivedAt;
    private bool _discarding;
    private int _discardedLength;

    /// <summary>
    /// 上限を超えて捨てた受信があった。値は捨てた文字数。
    ///
    /// <b>内容は渡さない。</b>読み取れなかったデータにも QR トークンが含まれうるため、
    /// 長さだけを残す（→ plan.md 15-1）。
    /// </summary>
    public event EventHandler<int>? Overflowed;

    /// <summary>確定していない受信中のデータがあるか。</summary>
    public bool HasPending => _pending.Length > 0;

    /// <summary>受信したバイト列を取り込み、確定したスキャン結果を返す。</summary>
    public IReadOnlyList<string> Append(byte[] data, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(data);

        _lastReceivedAt = now;

        var results = new List<string>();

        foreach (var b in data)
        {
            var c = (char)b;

            if (c is '\r' or '\n')
            {
                Complete(results);
                continue;
            }

            // 制御文字は捨てる。機種によっては STX/ETX などが混ざる。
            if (char.IsControl(c))
            {
                continue;
            }

            if (_pending.Length >= _maxLength)
            {
                // 上限を超えた分は、終端が来るまでまとめて捨てる。
                _discardedLength += _pending.Length + 1;
                _discarding = true;
                _pending.Clear();
                continue;
            }

            if (_discarding)
            {
                _discardedLength++;
                continue;
            }

            _pending.Append(c);
        }

        return results;
    }

    /// <summary>
    /// 終端が来ないまま一定時間経っていれば、溜まっている分を確定する。
    /// 定期的に呼ぶこと。
    /// </summary>
    public string? FlushIfIdle(DateTime now)
    {
        if (!HasPending || now - _lastReceivedAt < _idleTimeout)
        {
            return null;
        }

        var results = new List<string>();
        Complete(results);

        return results.Count > 0 ? results[0] : null;
    }

    /// <summary>受信途中の内容を捨てる。接続断や画面遷移のときに呼ぶ。</summary>
    public void Reset()
    {
        _pending.Clear();
        _discarding = false;
        _discardedLength = 0;
    }

    private void Complete(List<string> results)
    {
        var text = _pending.ToString().Trim();
        _pending.Clear();

        if (_discarding)
        {
            // 上限超過で壊れた1件。中途半端な文字列を打刻に使わせない。
            _discarding = false;

            Overflowed?.Invoke(this, _discardedLength);
            _discardedLength = 0;

            return;
        }

        // 空行は無視する。CRLF で終端する機種では 2 回続けて呼ばれる。
        if (text.Length > 0)
        {
            results.Add(text);
        }
    }
}
