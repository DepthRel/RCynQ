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
using RCynQ.Git.Commands;
using RCynQ.Git.Hashing;
using RCynQ.Git.Manifest;
using RCynQ.Git.Patches;
using RCynQ.Git.Patches.Diff;
using RCynQ.Git.Patches.Files;
using RCynQ.Git.Paths;
using RCynQ.Git.Processes;
using RCynQ.Git.Repository;
using RCynQ.Sync.Apply;
using RCynQ.Sync.Backups;
using RCynQ.Sync.Conflicts;
using RCynQ.Sync.Content;
using RCynQ.Sync.History;
using RCynQ.Sync.Locking;
using RCynQ.Sync.Paths;
using RCynQ.Sync.Security;
using RCynQ.Sync.Services;
using RCynQ.Sync.Signing;
using RCynQ.Sync.State;
using RCynQ.Sync.SyncBase;
using RCynQ.Sync.Tests.Fakes;
using RCynQ.Sync.Transfer;

namespace RCynQ.Sync.Tests.Fixtures;

/// <summary>
/// A Workstation node and an Origin node with temporary working trees, sync bases and mocked persistence.
/// </summary>
/// <remarks>
/// Initially every tree contains <c>a.txt = "v1\n"</c> and <c>b.txt = "keep\n"</c>.
/// </remarks>
public sealed class SyncFixture : IDisposable
{
    /// <summary>The shared link token.</summary>
    public const string TOKEN = "0123456789abcdef-link-token";

    /// <summary>
    /// The name the Workstation of the fixture knows its repository by; its patches carry it.
    /// </summary>
    public const string REPOSITORY_NAME = "app";

    /// <summary>The real Git repository service, shared by both nodes (stateless).</summary>
    public static readonly IGitRepositoryService GitRepository = new GitRepositoryService(TestTexts.English, new GitCommandRunner(new SystemProcessRunner(TestTexts.English), new GitOptions()));

    /// <summary>The Workstation node data directories.</summary>
    private readonly AppDataFactory _workstationAppData;

    /// <summary>The Origin node data directories.</summary>
    private readonly AppDataFactory _originAppData;

    /// <summary>The sync base manager of the Workstation node, created on first use.</summary>
    private SyncBaseManager? _workstationSyncBase;

    /// <summary>The sync base manager of the Origin node, created on first use.</summary>
    private SyncBaseManager? _originSyncBase;

    /// <summary>
    /// Initializes a new instance of the <see cref="SyncFixture"/> class.
    /// </summary>
    public SyncFixture()
    {
        Root = Path.Combine(Path.GetTempPath(), "rcynq-sync-tests", Guid.NewGuid().ToString("N"));
        WorkstationWork = Dir("workstation-work");
        WorkstationBaseForBuild = Dir("workstation-base");
        OriginWork = Dir("origin-work");
        WorkstationNode = new NodeConfig(new NodeId("workstation-1"), "Workstation", NodeRole.Workstation, null, WorkstationWork);
        OriginNode = new NodeConfig(new NodeId("origin-1"), "Origin", NodeRole.Origin, OriginWork, null);
        OriginOptions = new SyncBridgeOptions { NodeId = "origin-1", Role = NodeRole.Origin, Repositories = { new RepositoryOptions { Path = OriginWork } }, LinkToken = TOKEN };
        _workstationAppData = new AppDataFactory(AppDataPaths.FromRoot(Path.Combine(Root, "workstation-data")));
        _originAppData = new AppDataFactory(AppDataPaths.FromRoot(Path.Combine(Root, "origin-data")));
        WorkstationPaths = new SyncPaths(
            TestTexts.English,
            _workstationAppData,
            WorkstationNode,
            new FixedRepository(new RepositoryEntry(RepositoryEntry.DefaultId, REPOSITORY_NAME, WorkstationWork)));
        OriginPaths = new SyncPaths(TestTexts.English, _originAppData, OriginNode);
        Directory.CreateDirectory(OriginPaths.SyncBase);

        foreach (var dir in new[] { WorkstationWork, WorkstationBaseForBuild, OriginWork, OriginPaths.SyncBase })
        {
            Write(dir, "a.txt", "v1\n");
            Write(dir, "b.txt", "keep\n");
        }

        StateStore.GetAsync(Arg.Any<CancellationToken>()).Returns(new SyncState(null, null, null, null));
        DiffBuilder.BuildAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("diff --git a/a.txt b/a.txt\n"u8.ToArray());
    }

    /// <summary>Gets the temporary root.</summary>
    public string Root { get; }

    /// <summary>Gets the Workstation working tree.</summary>
    public string WorkstationWork { get; }

    /// <summary>Gets a Workstation base directory used to build envelopes directly.</summary>
    public string WorkstationBaseForBuild { get; }

    /// <summary>Gets the Origin working tree.</summary>
    public string OriginWork { get; }

    /// <summary>Gets the Workstation node.</summary>
    public NodeConfig WorkstationNode { get; }

    /// <summary>Gets the Origin node.</summary>
    public NodeConfig OriginNode { get; }

    /// <summary>Gets the Origin options.</summary>
    public SyncBridgeOptions OriginOptions { get; }

    /// <summary>Gets the Workstation sync paths.</summary>
    public SyncPaths WorkstationPaths { get; }

    /// <summary>Gets the Origin sync paths.</summary>
    public SyncPaths OriginPaths { get; }

    /// <summary>Gets the path utility.</summary>
    public PathUtility Paths { get; } = new(TestTexts.English);

    /// <summary>Gets the diff builder that works on the mocked git diff.</summary>
    public PatchDiffBuilder Diffs => new(DiffBuilder, Paths);

    /// <summary>Gets the applier that writes incoming content.</summary>
    public ContentOverwriteApplier Applier => new(TestTexts.English, Paths);

    /// <summary>Gets the Workstation signer.</summary>
    public EcdsaSignatureService WorkstationSigner { get; } = new(TestTexts.English, new InMemorySecretStore());

    /// <summary>Gets the Origin signer.</summary>
    public EcdsaSignatureService OriginSigner { get; } = new(TestTexts.English, new InMemorySecretStore());

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
        // Compared by content: a checkout may write CRLF where the test wrote LF (core.autocrlf of the machine).
        return File.Exists(full) ? File.ReadAllText(full).Replace("\r\n", "\n", StringComparison.Ordinal) : null;
    }

    /// <summary>
    /// Creates a patch builder.
    /// </summary>
    /// <returns>The builder.</returns>
    public DefaultPatchBuilder CreateBuilder()
        => new(new DefaultFileManifestBuilder(new SnapshotFilter()), new DefaultStateHasher(), TimeProvider.System);

    /// <summary>
    /// Builds an envelope from <see cref="WorkstationBaseForBuild"/> to <see cref="WorkstationWork"/>, signed by the Workstation key.
    /// </summary>
    /// <param name="target">The receiver, defaulting to Origin.</param>
    /// <returns>The envelope.</returns>
    public async Task<PatchEnvelope> BuildSignedEnvelopeAsync(NodeId? target = null)
    {
        var built = await CreateBuilder().BuildAsync(WorkstationNode.Id, target ?? OriginNode.Id, WorkstationBaseForBuild, WorkstationWork);
        var envelope = new PatchEnvelope(
            built.Id, built.SourceNode, built.TargetNode, built.CreatedAtUtc, built.BaseStateHash, built.IncomingStateHash, built.Manifest, repository: REPOSITORY_NAME);
        return envelope.WithSignature(await WorkstationSigner.SignAsync(PayloadCanonicalizer.Build(envelope)));
    }

    /// <summary>
    /// Registers the Workstation node as a known peer of Origin.
    /// </summary>
    /// <returns>A task representing the operation.</returns>
    public async Task RegisterWorkstationPeerAsync()
    {
        var key = Convert.ToBase64String(await WorkstationSigner.ExportPublicKeyAsync());
        Peers.GetAsync(WorkstationNode.Id, Arg.Any<CancellationToken>())
            .Returns(new PeerDescriptor(WorkstationNode.Id, "Workstation", NodeRole.Workstation, "workstation.local", 22, "rcynq", signingPublicKey: key));
    }

    /// <summary>
    /// Creates the Origin receiver.
    /// </summary>
    /// <returns>The receiver.</returns>
    public DefaultPatchReceiver CreateReceiver()
        => new(
            TestTexts.English,
            new PatchAdmission(TestTexts.English, OriginNode, Peers, OriginSigner, new LinkTokenProvider(OriginOptions, new InMemorySecretStore()), Patches),
            new IncomingPatchApplier(Storage, new PatchDisplayDiffStore(Diffs, Storage), new ConflictDetector(Paths), Applier, OriginPaths, OriginSyncBase),
            new ReplayGuard(TestTexts.English, Patches, TimeProvider.System, new ReplayGuardOptions()),
            Patches,
            Storage,
            CreateOutcomes(),
            CreateTracker(OriginNode),
            OriginLock,
            TimeProvider.System);

    /// <summary>
    /// Creates the Origin conflict resolver.
    /// </summary>
    /// <param name="git">The Git command runner.</param>
    /// <returns>The resolver.</returns>
    public DefaultConflictResolver CreateResolver(Git.Commands.IGitCommandRunner git)
    {
        var applier = Applier;
        var backups = new BackupService(OriginPaths, Paths, TimeProvider.System);
        return new DefaultConflictResolver(applier, new GitMergeApplier(new MergePlanner(git, Paths), new MergeOutcomeWriter(applier, backups)), backups, OriginPaths);
    }

    /// <summary>
    /// Creates a Workstation patch service with the given sender.
    /// </summary>
    /// <param name="sender">The sender.</param>
    /// <param name="chain">The chain repository.</param>
    /// <param name="repository">Tells which repository the node works with now; <see langword="null"/> for the one it started with.</param>
    /// <returns>The service.</returns>
    public PatchService CreateWorkstationService(IPatchSender sender, IChainLinkRepository chain, IActiveRepository? repository = null)
    {
        var outcomes = CreateOutcomes();
        var tracker = CreateTracker(WorkstationNode);
        var send = new PatchSendOperation(
            TestTexts.English,
            WorkstationNode,
            WorkstationPaths,
            chain,
            CreateBuilder(),
            WorkstationSigner,
            sender,
            new SyncBaseUpdater(TestTexts.English, Applier, Paths),
            new PatchDisplayDiffStore(Diffs, Storage),
            Paths,
            Patches,
            outcomes,
            tracker,
            WorkstationLock,
            WorkstationSyncBase);
        var resolve = new PatchResolveOperation(
            TestTexts.English,
            WorkstationNode,
            WorkstationPaths,
            Substitute.For<IConflictResolver>(),
            new ConflictDetector(Paths),
            Patches,
            Storage,
            outcomes,
            tracker,
            WorkstationLock,
            WorkstationSyncBase);
        return new PatchService(WorkstationNode, send, resolve, CreatePatchFileOperation(WorkstationNode), Substitute.For<IPatchReceiver>(), Patches, Storage, tracker, WorkstationSyncBase, repository);
    }

    /// <summary>
    /// Creates the operation on patch files of a node, on the real Git.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <returns>The operation.</returns>
    public PatchFileOperation CreatePatchFileOperation(NodeConfig node)
    {
        var runner = new GitCommandRunner(new SystemProcessRunner(TestTexts.English), new GitOptions());
        var isWorkstation = node.Id == WorkstationNode.Id;
        var appData = isWorkstation ? _workstationAppData : _originAppData;
        return new PatchFileOperation(
            TestTexts.English,
            node,
            isWorkstation ? WorkstationPaths : OriginPaths,
            CreateBuilder(),
            SyncBaseOf(node),
            new GitPatchFileService(runner, appData),
            new PatchDiffBuilder(new GitDiffBuilder(runner), Paths, appData),
            Paths,
            GitRepository,
            CreateTracker(node),
            LockOf(node),
            appData);
    }

    /// <summary>
    /// Gets the reader of the Workstation working tree, the content a sender streams.
    /// </summary>
    public WorkdirPatchContent WorkstationContent => new(TestTexts.English, WorkstationWork, Paths);

    /// <summary>
    /// Delivers an envelope to a receiver the way the sender does: opens the session, uploads the missing content from the
    /// Workstation working tree and commits.
    /// </summary>
    /// <param name="receiver">The receiver.</param>
    /// <param name="envelope">The signed envelope.</param>
    /// <param name="context">The request context.</param>
    /// <returns>The result of the commit.</returns>
    public async Task<PatchApplyResult> DeliverAsync(IPatchReceiver receiver, PatchEnvelope envelope, PatchReceiveContext context)
    {
        var session = await receiver.OpenSessionAsync(envelope, context);
        foreach (var hash in session.MissingContent)
        {
            var change = envelope.Manifest.First(candidate => candidate.Kind != ChangeKind.Deleted && candidate.Sha256 == hash);
            await using var stream = await WorkstationContent.OpenReadAsync(change);
            await receiver.ReceiveContentAsync(envelope.Id, hash, stream, context);
        }

        return await receiver.CommitAsync(envelope.Id, context);
    }

    /// <summary>
    /// Creates a chain from Workstation through one relay to Origin.
    /// </summary>
    /// <returns>The chain.</returns>
    public IReadOnlyList<ChainLinkConfig> CreateChain()
        => new[]
        {
            new ChainLinkConfig(Guid.NewGuid(), "hop", TransportKind.Direct, new PeerDescriptor(new NodeId("relay-1"), "Relay", NodeRole.Relay, "relay.example", 22, "jump"), 0, 22, 0),
            new ChainLinkConfig(Guid.NewGuid(), "endpoint", TransportKind.Direct, new PeerDescriptor(OriginNode.Id, "Origin", NodeRole.Origin, "origin.internal", 22, "rcynq"), 0, 18443, 1)
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
        catch (UnauthorizedAccessException)
        {
            // Git keeps object files read-only on some systems; best effort cleanup.
        }
    }

    /// <summary>
    /// Creates an outcome recorder.
    /// </summary>
    /// <returns>The recorder.</returns>
    private PatchOutcomeRecorder CreateOutcomes() => new(Patches, History, Events, TimeProvider.System);

    /// <summary>
    /// Gets the working tree lock of the Workstation node, shared by everything the fixture builds for it.
    /// </summary>
    public WorkingTreeLock WorkstationLock { get; } = new();

    /// <summary>
    /// Gets the working tree lock of the Origin node, shared by everything the fixture builds for it.
    /// </summary>
    public WorkingTreeLock OriginLock { get; } = new();

    /// <summary>
    /// Gets the sync base manager of the Workstation node, backed by the real Git plumbing so tests that <c>git init</c> the
    /// working tree exercise the Git sync base.
    /// </summary>
    public SyncBaseManager WorkstationSyncBase => _workstationSyncBase ??= CreateSyncBase(WorkstationNode, WorkstationPaths, _workstationAppData, WorkstationLock);

    /// <summary>
    /// Gets the sync base manager of the Origin node.
    /// </summary>
    public SyncBaseManager OriginSyncBase => _originSyncBase ??= CreateSyncBase(OriginNode, OriginPaths, _originAppData, OriginLock);

    /// <summary>
    /// Gets the working tree lock of <see cref="WorkstationNode"/> or <see cref="OriginNode"/>.
    /// </summary>
    /// <param name="node">Either node.</param>
    /// <returns>The lock.</returns>
    public WorkingTreeLock LockOf(NodeConfig node) => node.Id == WorkstationNode.Id ? WorkstationLock : OriginLock;

    /// <summary>
    /// Gets the sync base manager of <see cref="WorkstationNode"/> or <see cref="OriginNode"/>.
    /// </summary>
    /// <param name="node">Either node.</param>
    /// <returns>The manager.</returns>
    public SyncBaseManager SyncBaseOf(NodeConfig node) => node.Id == WorkstationNode.Id ? WorkstationSyncBase : OriginSyncBase;

    /// <summary>
    /// Creates a state tracker of <see cref="WorkstationNode"/> or <see cref="OriginNode"/>.
    /// </summary>
    /// <param name="node">Either node.</param>
    /// <returns>The tracker.</returns>
    public StateTracker CreateTracker(NodeConfig node)
        => new(
            StateStore,
            new DefaultFileManifestBuilder(new SnapshotFilter()),
            new DefaultStateHasher(),
            node.Id == WorkstationNode.Id ? WorkstationPaths : OriginPaths,
            node,
            TimeProvider.System,
            SyncBaseOf(node),
            LockOf(node));

    /// <summary>
    /// Creates a sync base manager.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <param name="paths">The paths.</param>
    /// <param name="appData">The node data directories.</param>
    /// <param name="workingTreeLock">The working tree lock of the node.</param>
    /// <returns>The manager.</returns>
    private SyncBaseManager CreateSyncBase(NodeConfig node, SyncPaths paths, AppDataFactory appData, WorkingTreeLock workingTreeLock)
        => new(
            TestTexts.English,
            node,
            paths,
            new GitSyncBaseLocator(TestTexts.English, node, GitRepository),
            GitRepository,
            new SyncBaseMetadataStore(appData),
            new DefaultFileManifestBuilder(new SnapshotFilter()),
            new DefaultStateHasher(),
            Substitute.For<ISnapshotRepository>(),
            Paths,
            workingTreeLock,
            TimeProvider.System);

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
