using RCynQ.Core.Abstractions.Storage;
using RCynQ.Core.Config;
using RCynQ.Core.Exceptions;
using RCynQ.Localization;

namespace RCynQ.Sync.Paths;

/// <summary>
/// Resolves the working tree and sync base directories of the local node.
/// </summary>
public sealed class SyncPaths
{
    /// <summary>
    /// The application data factory.
    /// </summary>
    private readonly IAppDataFactory _appData;

    /// <summary>
    /// The local node.
    /// </summary>
    private readonly NodeConfig _node;

    /// <summary>
    /// The localizer of the messages.
    /// </summary>
    private readonly ILocalizer _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="SyncPaths"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="appData">The application data factory.</param>
    /// <param name="node">The local node.</param>
    public SyncPaths(ILocalizer localizer, IAppDataFactory appData, NodeConfig node)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _appData = appData;
        _node = node;
    }

    /// <summary>
    /// Gets the working tree live patches are applied to.
    /// </summary>
    /// <exception cref="SyncBridgeException">The node is a Mediator.</exception>
    public string Workdir => Path.GetFullPath(_node.EffectiveWorkdir
        ?? throw new SyncBridgeException(_localizer.Format("Node.NoWorkingTree", _node.Role)));

    /// <summary>
    /// Gets the sync base directory.
    /// </summary>
    public string SyncBase => _appData.Create().SyncBaseDirectory;

    /// <summary>
    /// Gets the backups directory.
    /// </summary>
    public string Backups => _appData.Create().BackupsDirectory;
}
