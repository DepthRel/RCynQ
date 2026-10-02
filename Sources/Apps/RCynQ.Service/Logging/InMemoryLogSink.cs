using System.Collections.Concurrent;
using Serilog.Core;
using Serilog.Events;
using RCynQ.Core.Api;

namespace RCynQ.Service.Logging;

/// <summary>
/// Keeps the most recent log events in memory for the UI logs tab.
/// </summary>
public sealed class InMemoryLogSink : ILogEventSink
{
    /// <summary>
    /// The maximum number of retained entries.
    /// </summary>
    public const int CAPACITY = 2000;

    /// <summary>The retained entries.</summary>
    private readonly ConcurrentQueue<(LogEventLevel Level, LogEntryDto Entry)> _entries = new();

    /// <inheritdoc />
    public void Emit(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        _entries.Enqueue((logEvent.Level, new LogEntryDto(logEvent.Timestamp.ToUniversalTime(), logEvent.Level.ToString(), logEvent.RenderMessage(), logEvent.Exception?.ToString())));
        while (_entries.Count > CAPACITY && _entries.TryDequeue(out _))
        {
        }
    }

    /// <summary>
    /// Gets the newest entries at or above a level, newest first.
    /// </summary>
    /// <param name="minimumLevel">The minimum level.</param>
    /// <param name="take">The maximum number of entries.</param>
    /// <returns>The entries.</returns>
    public IReadOnlyList<LogEntryDto> GetEntries(LogEventLevel minimumLevel, int take)
        => _entries.Where(item => item.Level >= minimumLevel).Select(item => item.Entry).Reverse().Take(Math.Max(0, take)).ToArray();
}
