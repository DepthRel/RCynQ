namespace RCynQ.Sync.Tests.Fakes;

/// <summary>
/// A clock that stands still until a test moves it; its local time zone is UTC.
/// </summary>
public sealed class ManualClock : TimeProvider
{
    /// <summary>The current time.</summary>
    private DateTimeOffset _now;

    /// <summary>
    /// Initializes a new instance of the <see cref="ManualClock"/> class.
    /// </summary>
    /// <param name="now">The time it starts at.</param>
    public ManualClock(DateTimeOffset now) => _now = now;

    /// <inheritdoc />
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => _now;

    /// <summary>
    /// Moves the clock forward.
    /// </summary>
    /// <param name="by">How far.</param>
    public void Advance(TimeSpan by) => _now += by;
}
