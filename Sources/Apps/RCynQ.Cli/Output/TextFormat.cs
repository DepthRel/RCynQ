using System.Globalization;
using RCynQ.Core.Enums;

namespace RCynQ.Cli.Output;

/// <summary>
/// Formatting helpers for CLI output.
/// </summary>
public static class TextFormat
{
    /// <summary>
    /// Shortens a hash for display.
    /// </summary>
    /// <param name="hash">The hash, or <see langword="null"/>.</param>
    /// <returns>The first 12 characters or <c>-</c>.</returns>
    public static string Hash(string? hash) => string.IsNullOrEmpty(hash) ? "-" : hash[..Math.Min(12, hash.Length)];

    /// <summary>
    /// Formats a timestamp in local time.
    /// </summary>
    /// <param name="value">The timestamp, or <see langword="null"/>.</param>
    /// <returns>The formatted value or <c>never</c>.</returns>
    public static string Time(DateTimeOffset? value)
        => value is null ? "never" : value.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    /// <summary>
    /// Formats a change kind as a one-letter marker.
    /// </summary>
    /// <param name="kind">The change kind.</param>
    /// <returns><c>A</c>, <c>M</c>, <c>D</c> or <c>R</c>.</returns>
    public static string Change(ChangeKind kind) => kind switch
    {
        ChangeKind.Added => "A",
        ChangeKind.Modified => "M",
        ChangeKind.Deleted => "D",
        _ => "R"
    };
}
