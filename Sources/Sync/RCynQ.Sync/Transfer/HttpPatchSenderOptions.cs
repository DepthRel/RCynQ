namespace RCynQ.Sync.Transfer;

/// <summary>
/// Settings of the HTTP patch sender.
/// </summary>
public sealed class HttpPatchSenderOptions
{
    /// <summary>
    /// Gets or sets the timeout of one HTTP attempt.
    /// </summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Gets or sets the delay before the single network retry.
    /// </summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(2);
}
