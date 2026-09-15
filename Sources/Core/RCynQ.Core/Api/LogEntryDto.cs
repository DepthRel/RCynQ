namespace RCynQ.Core.Api;

/// <summary>
/// A log entry as returned by <c>GET /logs</c>.
/// </summary>
/// <param name="TimestampUtc">The time of the entry.</param>
/// <param name="Level">The level name: <c>Verbose</c>, <c>Debug</c>, <c>Information</c>, <c>Warning</c>, <c>Error</c> or <c>Fatal</c>.</param>
/// <param name="Message">The rendered message.</param>
/// <param name="Exception">The exception text, or <see langword="null"/>.</param>
public sealed record LogEntryDto(DateTimeOffset TimestampUtc, string Level, string Message, string? Exception);
