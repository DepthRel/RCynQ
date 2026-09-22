namespace RCynQ.Sync.Transfer;

/// <summary>
/// Settings of the HTTP patch sender.
/// </summary>
public sealed class HttpPatchSenderOptions
{
    /// <summary>
    /// Gets or sets the timeout of one HTTP attempt. Every file is a request of its own, so the limit applies per file.
    /// </summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Gets or sets the delay before the single network retry of a request.
    /// </summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Gets or sets the largest file the sender uploads, in bytes. The receiver accepts at most 512 MiB per request, and the
    /// compressed body of an incompressible file is slightly larger than the file.
    /// </summary>
    public long MaxFileBytes { get; set; } = 500L * 1024 * 1024;
}
