using System.Globalization;
using RCynQ.Core.Api;

namespace RCynQ.UI.ViewModels.Items;

/// <summary>
/// A log entry on the "Логи" tab.
/// </summary>
public sealed class LogItemViewModel
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LogItemViewModel"/> class.
    /// </summary>
    /// <param name="entry">The entry.</param>
    public LogItemViewModel(LogEntryDto entry)
    {
        Entry = entry ?? throw new ArgumentNullException(nameof(entry));
    }

    /// <summary>Gets the entry.</summary>
    public LogEntryDto Entry { get; }

    /// <summary>Gets the local time.</summary>
    public string TimeText => Entry.TimestampUtc.ToLocalTime().ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);

    /// <summary>Gets the level.</summary>
    public string Level => Entry.Level;

    /// <summary>Gets the message including the exception.</summary>
    public string Message => Entry.Exception is null ? Entry.Message : $"{Entry.Message}{Environment.NewLine}{Entry.Exception}";

    /// <summary>Gets a value indicating whether the entry is an error.</summary>
    public bool IsError => Entry.Level is "Error" or "Fatal";

    /// <summary>Gets a value indicating whether the entry is a warning.</summary>
    public bool IsWarning => Entry.Level == "Warning";

    /// <summary>
    /// Formats the entry as a single export line.
    /// </summary>
    /// <returns>The line.</returns>
    public string ToExportLine() => $"{Entry.TimestampUtc:O} [{Entry.Level}] {Message}";
}
