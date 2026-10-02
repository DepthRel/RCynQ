using System.Text.Json.Nodes;
using RCynQ.Core.Abstractions.Storage;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Git.Repository;
using RCynQ.Localization;
using RCynQ.Sync.Configuration;
using RCynQ.Sync.Locking;

namespace RCynQ.Sync.Repositories;

/// <summary>
/// The repositories of the node and the one it works with now.
/// </summary>
/// <remarks>
/// <para>
/// On an Origin the catalog registers Git repositories under names; a Workstation may ask for any of them, and for
/// nothing else on the computer. On a Workstation the catalog says in which local folder each repository is kept.
/// </para>
/// <para>
/// The catalog never writes into a repository. It keeps its entries in the configuration file of the node and removes
/// only what the node itself stored about a repository (its sync base in the data folder).
/// </para>
/// <para>
/// Switching waits for a running send, receive or Pull: it is done under the <see cref="WorkingTreeLock"/>.
/// </para>
/// </remarks>
public sealed class RepositoryCatalog : IActiveRepository
{
    /// <summary>The local node.</summary>
    private readonly NodeConfig _node;

    /// <summary>The options the entries are mirrored into, so the configuration the node reports stays current.</summary>
    private readonly SyncBridgeOptions _options;

    /// <summary>The lock of the working tree.</summary>
    private readonly WorkingTreeLock _lock;

    /// <summary>The application data factory.</summary>
    private readonly IAppDataFactory _appData;

    /// <summary>Where the entries are stored.</summary>
    private readonly RepositoryCatalogStorage _storage;

    /// <summary>The repository service, used to check a repository an Origin registers; <see langword="null"/> checks nothing.</summary>
    private readonly IGitRepositoryService? _git;

    /// <summary>The localizer of the messages.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>Serializes the changes of the catalog.</summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>The entries; replaced as a whole, never changed in place.</summary>
    private volatile IReadOnlyList<RepositoryEntry> _entries;

    /// <summary>The repository the node works with.</summary>
    private volatile RepositoryEntry? _current;

    /// <summary>
    /// Initializes a new instance of the <see cref="RepositoryCatalog"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="node">The local node.</param>
    /// <param name="options">The node options.</param>
    /// <param name="workingTreeLock">The lock of the working tree.</param>
    /// <param name="appData">The application data factory.</param>
    /// <param name="storage">Where the entries are stored; <see langword="null"/> keeps them in memory only.</param>
    /// <param name="git">The repository service that checks a repository an Origin registers; <see langword="null"/> checks nothing.</param>
    public RepositoryCatalog(
        ILocalizer localizer,
        NodeConfig node,
        SyncBridgeOptions options,
        WorkingTreeLock workingTreeLock,
        IAppDataFactory appData,
        RepositoryCatalogStorage? storage = null,
        IGitRepositoryService? git = null)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _node = node ?? throw new ArgumentNullException(nameof(node));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _lock = workingTreeLock ?? throw new ArgumentNullException(nameof(workingTreeLock));
        _appData = appData ?? throw new ArgumentNullException(nameof(appData));
        _storage = storage ?? new RepositoryCatalogStorage(null);
        _git = git;

        var configured = SyncBridgeOptionsValidator.ToRepositories(options);
        if (configured.Count > 0)
        {
            _entries = configured;
            _current = SyncBridgeOptionsValidator.ActiveRepositoryOf(options);
        }
        else
        {
            // A node described by its paths only (a tool, a test): the path it started with is its one repository.
            var single = FixedRepository.FromNode(node);
            _entries = single.Entries;
            _current = single.Current;
        }
    }

    /// <summary>
    /// Raised after the entries or the repository the node works with changed.
    /// </summary>
    public event EventHandler? Changed;

    /// <inheritdoc />
    public RepositoryEntry? Current => _current;

    /// <inheritdoc />
    public IReadOnlyList<RepositoryEntry> Entries => _entries;

    /// <summary>
    /// Finds the repository of a name.
    /// </summary>
    /// <param name="name">The name; may be <see langword="null"/>.</param>
    /// <returns>The entry, or <see langword="null"/>.</returns>
    public RepositoryEntry? Find(string? name) => _entries.FirstOrDefault(entry => entry.IsNamed(name?.Trim()));

    /// <summary>
    /// Adds a repository: an Origin registers a Git repository under a name, a Workstation says where it keeps the
    /// repository of that name.
    /// </summary>
    /// <param name="name">The name the nodes call the repository by.</param>
    /// <param name="path">The local path.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The new entry.</returns>
    /// <exception cref="SyncBridgeException">The name or the path cannot be used; the message tells why.</exception>
    public async Task<RepositoryEntry> AddAsync(string name, string path, CancellationToken cancellationToken = default)
    {
        EnsureHasRepositories();
        name = (name ?? string.Empty).Trim();
        if (!RepositoryName.IsValid(name))
        {
            throw Error("Config.RepositoryName", name);
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            throw Error("Repository.PathMissing");
        }

        var fullPath = Normalize(path);
        if (_node.Role == NodeRole.Origin && _git is not null &&
            (!Directory.Exists(fullPath) || !await _git.IsWorkTreeAsync(fullPath, cancellationToken)))
        {
            throw Error("Repository.NotGit", fullPath);
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (Find(name) is not null)
            {
                throw Error("Repository.Exists", name);
            }

            if (_entries.FirstOrDefault(entry => SamePath(entry.Path, fullPath)) is { } owner)
            {
                throw Error("Repository.PathUsed", fullPath, owner.IsBound ? owner.Name : owner.Path);
            }

            var id = _entries.Count == 0 ? RepositoryEntry.DefaultId : Guid.NewGuid();
            var entry = new RepositoryEntry(id, name, fullPath);
            var entries = _entries.Append(entry).ToArray();
            var current = _current ?? entry;
            await SaveAsync(entries, current, cancellationToken);
            _entries = entries;
            _current = current;
            Changed?.Invoke(this, EventArgs.Empty);
            return entry;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Removes a repository from the catalog together with what the node stored about it. The repository itself and its
    /// files are left exactly as they are.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the entry is removed.</returns>
    /// <exception cref="SyncBridgeException">There is no such repository, or the node works with it now.</exception>
    public async Task RemoveAsync(string name, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var entry = Find(name) ?? throw Error("Repository.Unknown", name);
            if (ReferenceEquals(entry, _current) || entry.Id == _current?.Id)
            {
                throw Error("Repository.RemoveActive", entry.Name);
            }

            var entries = _entries.Where(candidate => candidate.Id != entry.Id).ToArray();
            await SaveAsync(entries, _current, cancellationToken);
            _entries = entries;
            DeleteNodeData(entry);
            Changed?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Makes a repository the one the node works with. Waits for a running send, receive or Pull to finish.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The repository the node works with now.</returns>
    /// <exception cref="SyncBridgeException">There is no such repository.</exception>
    public async Task<RepositoryEntry> ActivateAsync(string name, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var entry = Find(name) ?? throw Error("Repository.Unknown", name);
            if (entry.Id == _current?.Id)
            {
                return entry;
            }

            using (await _lock.AcquireAsync(cancellationToken))
            {
                await SaveAsync(_entries, entry, cancellationToken);
                _current = entry;
            }

            Changed?.Invoke(this, EventArgs.Empty);
            return entry;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Gives the folder of a Workstation that has no name yet the name the Origin calls its repository by.
    /// </summary>
    /// <remarks>
    /// A node updated from a version that knew one repository, or set up before its first contact with the Origin, has
    /// a working copy but does not know its name. The first answer of the Origin tells it.
    /// </remarks>
    /// <param name="name">The name the Origin uses.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The bound entry, or <see langword="null"/> when there was nothing to bind or the name is taken or not valid.</returns>
    public async Task<RepositoryEntry?> BindAsync(string? name, CancellationToken cancellationToken = default)
    {
        name = name?.Trim();
        if (_node.Role != NodeRole.Workstation || !RepositoryName.IsValid(name) || _current is not { IsBound: false })
        {
            return null;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_current is not { IsBound: false } unbound || Find(name) is not null)
            {
                return null;
            }

            var bound = unbound.WithName(name!);
            var entries = _entries.Select(entry => entry.Id == unbound.Id ? bound : entry).ToArray();
            await SaveAsync(entries, bound, cancellationToken);
            _entries = entries;
            _current = bound;
            Changed?.Invoke(this, EventArgs.Empty);
            return bound;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Refuses the operation on a node that has no repositories by design.
    /// </summary>
    /// <exception cref="SyncBridgeException">The node is a Relay.</exception>
    private void EnsureHasRepositories()
    {
        if (_node.Role == NodeRole.Relay)
        {
            throw Error("Node.RelayNoPaths");
        }
    }

    /// <summary>
    /// Writes the entries to the configuration file and mirrors them into the options.
    /// </summary>
    /// <param name="entries">The entries.</param>
    /// <param name="current">The repository the node works with.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the entries are stored.</returns>
    private async Task SaveAsync(IReadOnlyList<RepositoryEntry> entries, RepositoryEntry? current, CancellationToken cancellationToken)
    {
        if (_storage.ConfigFilePath is { } path)
        {
            await ConfigurationFileEditor.UpdateAsync(
                path,
                section =>
                {
                    var array = new JsonArray();
                    foreach (var entry in entries)
                    {
                        array.Add(new JsonObject
                        {
                            [nameof(RepositoryOptions.Id)] = entry.Id.ToString(),
                            [nameof(RepositoryOptions.Name)] = entry.Name,
                            [nameof(RepositoryOptions.Path)] = entry.Path
                        });
                    }

                    ConfigurationFileEditor.SetNode(section, nameof(SyncBridgeOptions.Repositories), array);
                    ConfigurationFileEditor.SetProperty(section, nameof(SyncBridgeOptions.ActiveRepository), current is { IsBound: true } ? current.Name : null);
                },
                cancellationToken);
        }

        _options.Repositories = entries.Select(entry => new RepositoryOptions { Id = entry.Id, Name = entry.Name, Path = entry.Path }).ToList();
        _options.ActiveRepository = current is { IsBound: true } ? current.Name : null;
    }

    /// <summary>
    /// Deletes what the node stored about a repository: its sync base. Nothing in the repository is touched.
    /// </summary>
    /// <param name="entry">The removed entry.</param>
    private void DeleteNodeData(RepositoryEntry entry)
    {
        var paths = _appData.Create().ForRepository(entry.Id);
        try
        {
            if (Directory.Exists(paths.SyncBaseDirectory))
            {
                Directory.Delete(paths.SyncBaseDirectory, recursive: true);
            }

            File.Delete(paths.SyncBaseMetadataPath);
            if (entry.Id != RepositoryEntry.DefaultId && Directory.Exists(paths.SnapshotsDirectory) && !Directory.EnumerateFileSystemEntries(paths.SnapshotsDirectory).Any())
            {
                Directory.Delete(paths.SnapshotsDirectory);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Data of the node only; what is left is rebuilt or ignored, and the entry is gone either way.
        }
    }

    /// <summary>
    /// Creates the exception of a refused operation.
    /// </summary>
    /// <param name="key">The key of the message.</param>
    /// <param name="args">The arguments of the message.</param>
    /// <returns>The exception.</returns>
    private static SyncBridgeException Error(string key, params object?[] args)
        => LocalizedError.Create(message => new SyncBridgeException(message), key, args);

    /// <summary>
    /// Brings a path to its full form without a trailing separator.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <returns>The normalized path.</returns>
    private static string Normalize(string path)
        => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim()));

    /// <summary>
    /// Tells whether two paths mean the same folder.
    /// </summary>
    /// <param name="left">The first path.</param>
    /// <param name="right">The second path.</param>
    /// <returns><see langword="true"/> when they are the same folder.</returns>
    private static bool SamePath(string left, string right)
        => string.Equals(Normalize(left), Normalize(right), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
