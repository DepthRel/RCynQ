namespace RCynQ.Sync.Security;

/// <summary>
/// Settings of the replay guard.
/// </summary>
public sealed class ReplayGuardOptions
{
    /// <summary>
    /// Gets or sets the maximum accepted envelope age.
    /// </summary>
    public TimeSpan MaxAge { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// Gets or sets the tolerated clock skew for envelopes from the future.
    /// </summary>
    public TimeSpan MaxClockSkew { get; set; } = TimeSpan.FromMinutes(5);
}
