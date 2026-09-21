using NSubstitute;
using RCynQ.Core.Abstractions.Events;
using RCynQ.Core.Abstractions.Patches;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Config;
using RCynQ.Core.Data.Storage;
using RCynQ.Core.Enums;
using RCynQ.Core.Ids;
using RCynQ.Core.Patches;
using RCynQ.Core.State;
using RCynQ.Git.Hashing;
using RCynQ.Git.Manifest;
using RCynQ.Git.Patches;
using RCynQ.Git.Patches.Bundles;
using RCynQ.Git.Patches.Diff;
using RCynQ.Git.Paths;
using RCynQ.Sync.Apply;
using RCynQ.Sync.Backups;
using RCynQ.Sync.Baseline;
using RCynQ.Sync.Conflicts;
using RCynQ.Sync.History;
using RCynQ.Sync.Locking;
using RCynQ.Sync.Paths;
using RCynQ.Sync.Security;
using RCynQ.Sync.Services;
using RCynQ.Sync.Signing;
using RCynQ.Sync.State;
using RCynQ.Sync.Tests.Fakes;
using RCynQ.Sync.Transfer;

namespace RCynQ.Sync.Tests.Fixtures;

/// <summary>
/// A Source node and a Dest node with temporary working trees, sync bases and mocked persistence.
/// </summary>
/// <remarks>
/// Initially every tree contains <c>a.txt = "v1\n"</c> and <c>b.txt = "keep\n"</c>.
/// </remarks>
public sealed class SyncFixture : IDisposable
{
    /// <summary>The shared link token.</summary>
    public const string Token = "0123456789abcdef-link-token";

    /// <summary>
    /// Initializes a new instance of the <see cref="SyncFixture"/> class.
    /// </summary>
    public SyncFixture()
    {
        Root = Path.Combine(Path.GetTempPath(), "rcynq-sync-tests", Guid.NewGuid().ToString("N"));
        SourceWork = Dir("source-work");
        SourceBaseForBuild = Dir("source-base");
        DestWork = Dir("dest-work");
        SourceNode = new NodeConfig(new NodeId("source-1"), "Source", NodeRole.Source, null, SourceWork);
        DestNode = new NodeConfig(new NodeId("dest-1"), "Dest", NodeRole.Dest, DestWork, null);
        DestOptions = new SyncBridgeOptions { NodeId = "dest-1", Role = NodeRole.Dest, RepoPath = DestWork, LinkToken = Token };
        SourcePaths = new SyncPaths(new AppDataFactory(AppDataPaths.FromRoot(Path.Combine(Root, "source-data"))), SourceNode);
        DestPaths = new SyncPaths(new AppDataFactory(AppDataPaths.FromRoot(Path.Combine(Root, "dest-data"))), DestNode);
        Directory.CreateDirectory(DestPaths.SyncBase);

        foreach (var dir in new[] { SourceWork, SourceBaseForBuild, DestWork, DestPaths.SyncBase })
        {
            Write(dir, "a.txt", "v1\n");
            Write(dir, "b.txt", "keep\n");
        }

        StateStore.GetAsync(Arg.Any<CancellationToken>()).Returns(new SyncState(null, null, null, null));
        DiffBuilder.BuildAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("diff --git a/a.txt b/a.txt\n"u8.ToArray());
    }

    /// <summary>Gets the temporary root.</summary>
    public string Root { get; }

    /// <summary>Gets the Source working tree.</summary>
    public string SourceWork { get; }

    /// <summary>Gets a Source base directory used to build envelopes directly.</summary>
    public string SourceBaseForBuild { get; }

    /// <summary>Gets the Dest working tree.</summary>
    public string DestWork { get; }

    /// <summary>Gets the Source node.</summary>
    public NodeConfig SourceNode { get; }

    /// <summary>Gets the Dest node.</summary>
    public NodeConfig DestNode { get; }

    /// <summary>Gets the Dest options.</summary>
    public SyncBridgeOptions DestOptions { get; }

    /// <summary>Gets the Source sync paths.</summary>
    public SyncPaths SourcePaths { get; }

    /// <summary>Gets the Dest sync paths.</summary>
    public SyncPaths DestPaths { get; }

    /// <summary>Gets the path utility.</summary>
    public PathUtility Paths { get; } = new();

    /// <summary>Gets the bundle builder.</summary>
    public FileBundleBuilder Bundles => new(Paths);

    /// <summary>Gets the Source signer.</summary>
    public EcdsaSignatureService SourceSigner { get; } = new(new InMemorySecretStore());

    /// <summary>Gets the Dest signer.</summary>
    public EcdsaSignatureService DestSigner { get; } = new(new InMemorySecretStore());

    /// <summary>Gets the patch repository.</summary>
    public InMemoryPatchRepository Patches { get; } = new();

    /// <summary>Gets the payload storage.</summary>
    public InMemoryPatchFileStorage Storage { get; } = new();

    /// <summary>Gets the history repository mock.</summary>
    public ISyncHistoryRepository History { get; } = Substitute.For<ISyncHistoryRepository>();

    /// <summary>Gets the event publisher mock.</summary>
    public IEventPublisher Events { get; } = Substitute.For<IEventPublisher>();

    /// <summary>Gets the state store mock.</summary>
    public INodeStateStore StateStore { get; } = Substitute.For<INodeStateStore>();

    /// <summary>Gets the peer repository mock.</summary>
    public IPeerRepository Peers { get; } = Substitute.For<IPeerRepository>();

    /// <summary>Gets the diff builder mock.</summary>
    public IGitDiffBuilder DiffBuilder { get; } = Substitute.For<IGitDiffBuilder>();

    /// <summary>
    /// Writes a file below a directory.
    /// </summary>
    /// <param name="directory">The directory.</param>
    /// <param name="relative">The relative path.</param>
    /// <param name="content">The content.</param>
    public static void Write(string directory, string relative, string content)
    {
        var full = Path.Combine(directory, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    /// <summary>
    /// Reads a file below a directory, or <see langword="null"/> when it does not exist.
    /// </summary>
    /// <param name="directory">The directory.</param>
    /// <param name="relative">The relative path.</param>
    /// <returns>The content.</returns>
    public static string? Read(string directory, string relative)
    {
        var full = Path.Combine(directory, relative);
        return File.Exists(full) ? File.ReadAllText(full) : null;
    }

    /// <summary>
    /// Creates a patch builder with the mocked diff builder.
    /// </summary>
    /// <returns>The builder.</returns>
    public DefaultPatchBuilder CreateBuilder()
        => new(new DefaultFileManifestBuilder(new SnapshotFilter()), new DefaultStateHasher(), DiffBuilder, Bundles, Paths, TimeProvider.System);

    /// <summary>
    /// Builds an envelope from <see cref="SourceBaseForBuild"/> to <see cref="SourceWork"/>, signed by the Source key.
    /// </summary>
    /// <param name="target">The receiver, defaulting to Dest.</param>
    /// <returns>The envelope.</returns>
    public async Task<PatchEnvelope> BuildSignedEnvelopeAsync(NodeId? target = null)
    {
        var envelope = await CreateBuilder().BuildAsync(SourceNode.Id, target ?? DestNode.Id, SourceBaseForBuild, SourceWork);
        return envelope.WithSignature(await SourceSigner.SignAsync(PayloadCanonicalizer.Build(envelope)));
    }

    /// <summary>
    /// Registers the Source node as a known peer of Dest.
    /// </summary>
    /// <returns>A task representing the operation.</returns>
    public async Task RegisterSourcePeerAsync()
    {
        var key = Convert.ToBase64String(await SourceSigner.ExportPublicKeyAsync());
        Peers.GetAsync(SourceNode.Id, Arg.Any<CancellationToken>())
            .Returns(new PeerDescriptor(SourceNode.Id, "Source", NodeRole.Source, "source.local", 22, "rcynq", signingPublicKey: key));
    }

    /// <summary>
    /// Creates the Dest receiver.
    /// </summary>
    /// <returns>The receiver.</returns>
    public DefaultPatchReceiver CreateReceiver()
        => new(
            DestNode,
            Peers,
            DestSigner,
            new ReplayGuard(Patches, TimeProvider.System, new ReplayGuardOptions()),
            new LinkTokenProvider(DestOptions, new InMemorySecretStore()),
            Patches,
            Storage,
            Bundles,
            new ConflictDetector(Paths),
            new BundleOverwriteApplier(Bundles, Paths),
            CreateOutcomes(),
            CreateTracker(DestNode, DestPaths),
            DestPaths,
            new WorkingTreeLock());

    /// <summary>
    /// Creates the Dest conflict resolver.
    /// </summary>
    /// <param name="git">The git runner mock.</param>
    /// <returns>The resolver.</returns>
    public DefaultConflictResolver CreateResolver(Git.Commands.IGitCommandRunner git)
    {
        var applier = new BundleOverwriteApplier(Bundles, Paths);
        return new DefaultConflictResolver(
            applier,
            new GitPatchApplier(git, new Git.Repository.GitRepositoryService(git), Paths),
            new BackupService(DestPaths, Paths, TimeProvider.System),
            DestPaths);
    }

    /// <summary>
    /// Creates a Source patch service with the given sender.
    /// </summary>
    /// <param name="sender">The sender.</param>
    /// <param name="chain">The chain repository.</param>
    /// <returns>The service.</returns>
    public PatchService CreateSourceService(IPatchSender sender, IChainLinkRepository chain)
    {
        var workingTreeLock = new WorkingTreeLock();
        var tracker = CreateTracker(SourceNode, SourcePaths);
        var manifest = new DefaultFileManifestBuilder(new SnapshotFilter());
        return new PatchService(
            SourceNode,
            SourcePaths,
            chain,
            CreateBuilder(),
            SourceSigner,
            sender,
            Substitute.For<IPatchReceiver>(),
            Substitute.For<IConflictResolver>(),
            new ConflictDetector(Paths),
            new BundleOverwriteApplier(Bundles, Paths),
            Patches,
            Storage,
            CreateOutcomes(),
            tracker,
            new BaselineService(SourceNode, SourcePaths, manifest, new DefaultStateHasher(), Substitute.For<ISnapshotRepository>(), Paths, tracker, workingTreeLock, TimeProvider.System),
            workingTreeLock);
    }

    /// <summary>
    /// Creates a chain from Source through one mediator to Dest.
    /// </summary>
    /// <returns>The chain.</returns>
    public IReadOnlyList<ChainLinkConfig> CreateChain()
        => new[]
        {
            new ChainLinkConfig(Guid.NewGuid(), "hop", TransportKind.Direct, new PeerDescriptor(new NodeId("mediator-1"), "Relay", NodeRole.Mediator, "relay.example", 22, "jump"), 0, 22, 0),
            new ChainLinkConfig(Guid.NewGuid(), "endpoint", TransportKind.Direct, new PeerDescriptor(DestNode.Id, "Dest", NodeRole.Dest, "dest.internal", 22, "rcynq"), 0, 18443, 1)
        };

    /// <summary>
    /// Deletes the temporary directories.
    /// </summary>
    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // Best effort cleanup.
        }
    }

    /// <summary>
    /// Creates an outcome recorder.
    /// </summary>
    /// <returns>The recorder.</returns>
    private PatchOutcomeRecorder CreateOutcomes() => new(Patches, History, Events, TimeProvider.System);

    /// <summary>
    /// Creates a state tracker.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <param name="paths">The paths.</param>
    /// <returns>The tracker.</returns>
    private StateTracker CreateTracker(NodeConfig node, SyncPaths paths)
        => new(StateStore, new DefaultFileManifestBuilder(new SnapshotFilter()), new DefaultStateHasher(), paths, node, TimeProvider.System);

    /// <summary>
    /// Creates a directory below the root.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <returns>The full path.</returns>
    private string Dir(string name)
    {
        var path = Path.Combine(Root, name);
        Directory.CreateDirectory(path);
        return path;
    }
}
