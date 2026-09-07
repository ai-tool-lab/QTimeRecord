using Microsoft.Extensions.Logging;

namespace QTimeRecord.Tests;

/// <summary>書き出されたログを捉える。何が残るかを検証するために使う。</summary>
public sealed class CapturingLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, string Message)> Entries { get; } = [];

    public IEnumerable<string> Messages => Entries.Select(e => e.Message);

    IDisposable? ILogger.BeginScope<TState>(TState state) => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);

        Entries.Add((logLevel, formatter(state, exception)));
    }

    /// <summary>その文字列を含むログが1件でもあるか。</summary>
    public bool Contains(string text)
        => Messages.Any(m => m.Contains(text, StringComparison.Ordinal));
}
