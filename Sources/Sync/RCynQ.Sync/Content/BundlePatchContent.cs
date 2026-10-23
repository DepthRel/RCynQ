using RCynQ.Core.Abstractions.Patches;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Patches;
using RCynQ.Localization;

namespace RCynQ.Sync.Content;

/// <summary>
/// The content of a delivery of commits on the sender: the one bundle file it uploads.
/// </summary>
public sealed class BundlePatchContent : IPatchContent
{
    /// <summary>The size of the read buffer.</summary>
    private const int BUFFER_SIZE = 81920;

    /// <summary>The localizer of the messages.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>The commits the bundle holds.</summary>
    private readonly CommitDelivery _commits;

    /// <summary>The bundle file.</summary>
    private readonly string _bundlePath;

    /// <summary>
    /// Initializes a new instance of the <see cref="BundlePatchContent"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="commits">The commits the bundle holds.</param>
    /// <param name="bundlePath">The bundle file.</param>
    public BundlePatchContent(ILocalizer localizer, CommitDelivery commits, string bundlePath)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _commits = commits ?? throw new ArgumentNullException(nameof(commits));
        _bundlePath = bundlePath ?? throw new ArgumentNullException(nameof(bundlePath));
    }

    /// <inheritdoc />
    public Task<Stream> OpenReadAsync(FileChange change, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (!string.Equals(change.Sha256, _commits.BundleSha256, StringComparison.Ordinal) || !File.Exists(_bundlePath))
        {
            throw new SyncBridgeException(_localizer.Format("Commits.BundleMissing", change.Sha256 ?? string.Empty));
        }

        return Task.FromResult<Stream>(new FileStream(_bundlePath, FileMode.Open, FileAccess.Read, FileShare.Read, BUFFER_SIZE, useAsync: true));
    }
}
