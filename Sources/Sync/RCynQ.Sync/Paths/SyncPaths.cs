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

    /// <summary>Tells which repository the node works with now.</summary>
    private readonly IActiveRepository _repository;

    /// <summary>
    /// Initializes a new instance of the <see cref="SyncPaths"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="appData">The application data factory.</param>
    /// <param name="node">The local node.</param>
    /// <param name="repository">Tells which repository the node works with now; <see langword="null"/> for the one the node started with.</param>
    public SyncPaths(ILocalizer localizer, IAppDataFactory appData, NodeConfig node, IActiveRepository? repository = null)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _appData = appData;
        _node = node;
        _repository = repository ?? FixedRepository.FromNode(node);
    }

    /// <summary>
    /// Gets the working tree live patches are applied to.
    /// </summary>
    /// <exception cref="SyncBridgeException">The node is a Relay.</exception>
    public string Workdir => Path.GetFullPath(_repository.Current?.Path
        ?? throw new SyncBridgeException(_localizer.Format("Node.NoWorkingTree", _node.Role)));

    /// <summary>
    /// Gets the repository the node works with now, or <see langword="null"/> when it has none.
    /// </summary>
    public RepositoryEntry? Repository => _repository.Current;

    /// <summary>
    /// Gets the paths of the data the node keeps about the repository it works with now.
    /// </summary>
    public AppDataPaths Data => _appData.Create().ForRepository(_repository.Current?.Id ?? RepositoryEntry.DefaultId);

    /// <summary>
    /// Gets the sync base directory.
    /// </summary>
    public string SyncBase => Data.SyncBaseDirectory;

    /// <summary>
    /// Gets the backups directory.
    /// </summary>
    public string Backups => _appData.Create().BackupsDirectory;
}
