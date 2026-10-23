using RCynQ.Core.Config;

namespace RCynQ.Sync.Commits;

/// <summary>
/// Tells whether this Origin takes commits from the Workstation now; the user switches it in the settings while the
/// service runs.
/// </summary>
public sealed class CommitAcceptance
{
    /// <summary>
    /// Whether commits are taken; read and written from several threads.
    /// </summary>
    private volatile bool _enabled;

    /// <summary>
    /// Initializes a new instance of the <see cref="CommitAcceptance"/> class.
    /// </summary>
    /// <param name="options">The node options; <see cref="SyncBridgeOptions.AcceptCommits"/> gives the value at the start.</param>
    public CommitAcceptance(SyncBridgeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _enabled = options.AcceptCommits;
    }

    /// <summary>
    /// Gets or sets a value indicating whether commits from the Workstation are taken.
    /// </summary>
    public bool IsEnabled
    {
        get => _enabled;
        set => _enabled = value;
    }
}
