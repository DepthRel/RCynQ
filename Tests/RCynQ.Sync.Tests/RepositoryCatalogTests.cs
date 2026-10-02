using System.Text.Json.Nodes;
using FluentAssertions;
using NSubstitute;
using RCynQ.Core.Abstractions.Events;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Abstractions.Storage;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Events;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Ids;
using RCynQ.Core.Patches;
using RCynQ.Git.Repository;
using RCynQ.Localization;
using RCynQ.Sync.GitChannel;
using RCynQ.Sync.Locking;
using RCynQ.Sync.Paths;
using RCynQ.Sync.Repositories;
using RCynQ.Sync.Security;
using RCynQ.Sync.SyncBase;
using RCynQ.Sync.Tests.Fakes;
using RCynQ.Sync.Tests.Fixtures;
using RCynQ.Sync.Transfer;

namespace RCynQ.Sync.Tests;

/// <summary>
/// Tests of the repositories of a node: the catalog, switching between repositories and what the nodes tell each other
/// about them.
/// </summary>
public sealed class RepositoryCatalogTests : IDisposable
{
    /// <summary>The temporary directory.</summary>
    private readonly string _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "rcynq-catalog-tests", Guid.NewGuid().ToString("N"))).FullName;

    /// <summary>The repository service substitute; every existing directory is a Git repository for it.</summary>
    private readonly IGitRepositoryService _git = Substitute.For<IGitRepositoryService>();

    /// <summary>The application data factory.</summary>
    private readonly IAppDataFactory _appData = Substitute.For<IAppDataFactory>();

    /// <summary>
    /// Initializes a new instance of the <see cref="RepositoryCatalogTests"/> class.
    /// </summary>
    public RepositoryCatalogTests()
    {
        _git.IsWorkTreeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call => Directory.Exists(Path.Combine(call.Arg<string>(), ".git")));
        _appData.Create().Returns(AppDataPaths.FromRoot(Path.Combine(_root, "data")));
    }

    /// <inheritdoc />
    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <summary>
    /// Verifies that an Origin registers Git repositories under names, keeps them in the configuration file and refuses
    /// what cannot be a repository.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Origin_Registers_Repositories_And_Stores_Them()
    {
        var (catalog, configPath) = CreateOrigin();
        var tools = Repository("tools");

        var added = await catalog.AddAsync(" tools ", tools);

        added.Name.Should().Be("tools");
        added.Id.Should().NotBe(RepositoryEntry.DefaultId, "only the repository the node started with keeps the default place");
        catalog.Entries.Select(entry => entry.Name).Should().Equal("app", "tools");
        catalog.Current!.Name.Should().Be("app", "adding a repository does not switch to it");
        var section = JsonNode.Parse(await File.ReadAllTextAsync(configPath))!["SyncBridge"]!;
        section["Repositories"]!.AsArray().Select(node => node!["Name"]!.GetValue<string>()).Should().Equal("app", "tools");
        section["ActiveRepository"]!.GetValue<string>().Should().Be("app");
        section["NodeId"]!.GetValue<string>().Should().Be("origin-1", "other settings are kept");

        await Refused(() => catalog.AddAsync("TOOLS", Repository("another"))).Should().ContainAsync("already in the list");
        await Refused(() => catalog.AddAsync("copy", tools)).Should().ContainAsync("already belongs");
        await Refused(() => catalog.AddAsync("a/b", Repository("slash"))).Should().ContainAsync("not valid");
        await Refused(() => catalog.AddAsync("plain", Directory.CreateDirectory(Path.Combine(_root, "plain")).FullName)).Should().ContainAsync("not a Git repository");
        await Refused(() => catalog.AddAsync("missing", Path.Combine(_root, "missing"))).Should().ContainAsync("not a Git repository");
        await Refused(() => catalog.AddAsync("empty", " ")).Should().ContainAsync("folder");
    }

    /// <summary>
    /// Verifies that switching changes the working tree and the place of the sync base at once, and is stored.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Switching_Changes_The_Working_Tree_And_The_Sync_Base_Without_A_Restart()
    {
        var (catalog, configPath) = CreateOrigin();
        var tools = Repository("tools");
        await catalog.AddAsync("tools", tools);
        var paths = new SyncPaths(TestTexts.English, _appData, OriginNode(), catalog);
        var appBase = paths.SyncBase;
        var changes = 0;
        catalog.Changed += (_, _) => changes++;

        var active = await catalog.ActivateAsync("Tools");

        active.Path.Should().Be(tools);
        paths.Workdir.Should().Be(tools);
        paths.SyncBase.Should().NotBe(appBase).And.StartWith(Path.Combine(_root, "data", "snapshots"));
        changes.Should().Be(1);
        JsonNode.Parse(await File.ReadAllTextAsync(configPath))!["SyncBridge"]!["ActiveRepository"]!.GetValue<string>().Should().Be("tools");

        await catalog.ActivateAsync("tools");
        changes.Should().Be(1, "switching to the current repository changes nothing");
        await Refused(() => catalog.ActivateAsync("gone")).Should().ContainAsync("no repository 'gone'");

        await catalog.ActivateAsync("app");
        paths.SyncBase.Should().Be(appBase, "the repository the node started with keeps its sync base where it was");
    }

    /// <summary>
    /// Verifies that switching waits for the work on the working tree that is running.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Switching_Waits_For_Running_Work_On_The_Working_Tree()
    {
        var workingTreeLock = new WorkingTreeLock();
        var (catalog, _) = CreateOrigin(workingTreeLock);
        await catalog.AddAsync("tools", Repository("tools"));
        var busy = await workingTreeLock.AcquireAsync();

        var switching = catalog.ActivateAsync("tools");
        await Task.Delay(150);
        switching.IsCompleted.Should().BeFalse();
        catalog.Current!.Name.Should().Be("app");

        busy.Dispose();
        (await switching).Name.Should().Be("tools");
    }

    /// <summary>
    /// Verifies that removing a repository deletes what the node stored about it and nothing of the repository itself,
    /// and that the repository the node works with cannot be removed.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Removing_Deletes_Node_Data_Only()
    {
        var (catalog, _) = CreateOrigin();
        var tools = Repository("tools");
        var entry = await catalog.AddAsync("tools", tools);
        var data = _appData.Create().ForRepository(entry.Id);
        SyncFixture.Write(data.SyncBaseDirectory, "a.txt", "base");
        Directory.CreateDirectory(Path.GetDirectoryName(data.SyncBaseMetadataPath)!);
        await File.WriteAllTextAsync(data.SyncBaseMetadataPath, "{}");
        var before = Snapshot(tools);

        await Refused(() => catalog.RemoveAsync("app")).Should().ContainAsync("Switch to another repository first");
        await catalog.RemoveAsync("tools");

        catalog.Entries.Select(candidate => candidate.Name).Should().Equal("app");
        Directory.Exists(data.SnapshotsDirectory).Should().BeFalse();
        Snapshot(tools).Should().Equal(before, "the repository and its files are not the node's to delete");
        await Refused(() => catalog.RemoveAsync("tools")).Should().ContainAsync("no repository 'tools'");
    }

    /// <summary>
    /// Verifies that the folder of a Workstation that has no name takes the name the Origin uses, once.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Workstation_Folder_Takes_The_Name_The_Origin_Uses()
    {
        var work = Directory.CreateDirectory(Path.Combine(_root, "work")).FullName;
        var options = new SyncBridgeOptions { NodeId = "workstation-1", Role = NodeRole.Workstation, Repositories = { new RepositoryOptions { Path = work } } };
        var node = SyncBridgeOptionsValidator.ToNodeConfig(options);
        var catalog = new RepositoryCatalog(TestTexts.English, node, options, new WorkingTreeLock(), _appData);
        var events = Substitute.For<IEventPublisher>();
        var tracker = new PeerRepositoryTracker(catalog, events, TimeProvider.System);

        catalog.Current!.IsBound.Should().BeFalse();
        await tracker.RecordAsync("not a name");
        await tracker.RecordAsync(null);
        tracker.PeerRepository.Should().BeNull();

        await tracker.RecordAsync("app");

        catalog.Current!.Name.Should().Be("app");
        catalog.Current.Id.Should().Be(RepositoryEntry.DefaultId, "the folder keeps its sync base and history");
        options.ActiveRepository.Should().Be("app");
        tracker.IsMismatch.Should().BeFalse();
        await events.Received(1).PublishRepositoryAsync(Arg.Is<RepositoryEvent>(e => e.Repository == "app" && e.PeerRepository == "app"), Arg.Any<CancellationToken>());

        await tracker.RecordAsync("tools");

        catalog.Current.Name.Should().Be("app", "a node never follows the other one by itself");
        tracker.IsMismatch.Should().BeTrue();
        await events.Received(1).PublishRepositoryAsync(Arg.Is<RepositoryEvent>(e => e.Repository == "app" && e.PeerRepository == "tools"), Arg.Any<CancellationToken>());

        await tracker.RecordAsync("TOOLS");
        events.ReceivedCalls().Should().HaveCount(2, "the same repository again is no news");
    }

    /// <summary>
    /// Verifies that a patch for a repository the node does not work with now is refused before anything is applied.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Patch_For_Another_Repository_Is_Refused()
    {
        using var fixture = new SyncFixture();
        await fixture.RegisterWorkstationPeerAsync();
        SyncFixture.Write(fixture.WorkstationWork, "a.txt", "v2\n");
        var envelope = await fixture.BuildSignedEnvelopeAsync();
        var context = new PatchReceiveContext(fixture.WorkstationNode.Id.Value, SyncFixture.TOKEN);
        PatchAdmission Admission(string repository) => new(
            TestTexts.English,
            fixture.OriginNode,
            fixture.Peers,
            fixture.OriginSigner,
            new LinkTokenProvider(fixture.OriginOptions, new InMemorySecretStore()),
            fixture.Patches,
            new FixedRepository(new RepositoryEntry(RepositoryEntry.DefaultId, repository, fixture.OriginWork)));

        envelope.Repository.Should().Be(SyncFixture.REPOSITORY_NAME);
        await Admission(SyncFixture.REPOSITORY_NAME).ValidateAsync(envelope, context);

        var refuse = () => Admission("tools").ValidateAsync(envelope, context);
        var rejection = (await refuse.Should().ThrowAsync<PatchRejectedException>()).Which;
        rejection.Reason.Should().Be(PatchRejectionReason.RepositoryMismatch);
        rejection.Message.Should().Contain("'tools'").And.Contain($"'{SyncFixture.REPOSITORY_NAME}'");
    }

    /// <summary>
    /// Verifies that the name of the repository is part of what is signed, so it cannot be changed on the way.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Repository_Name_Is_Covered_By_The_Signature()
    {
        using var fixture = new SyncFixture();
        await fixture.RegisterWorkstationPeerAsync();
        SyncFixture.Write(fixture.WorkstationWork, "a.txt", "v2\n");
        var envelope = await fixture.BuildSignedEnvelopeAsync();
        var redirected = new PatchEnvelope(
            envelope.Id, envelope.SourceNode, envelope.TargetNode, envelope.CreatedAtUtc, envelope.BaseStateHash, envelope.IncomingStateHash, envelope.Manifest, envelope.Signature, "tools");
        var admission = new PatchAdmission(
            TestTexts.English, fixture.OriginNode, fixture.Peers, fixture.OriginSigner, new LinkTokenProvider(fixture.OriginOptions, new InMemorySecretStore()), fixture.Patches);

        var validate = () => admission.ValidateAsync(redirected, new PatchReceiveContext(fixture.WorkstationNode.Id.Value, SyncFixture.TOKEN));

        (await validate.Should().ThrowAsync<PatchRejectedException>()).Which.Reason.Should().Be(PatchRejectionReason.InvalidSignature);
    }

    /// <summary>
    /// Verifies that an Origin serves a bundle of any registered repository by its name, of the current one when no name
    /// is given, and of nothing else.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Bundle_Is_Served_For_A_Registered_Repository_By_Name()
    {
        var (catalog, _) = CreateOrigin();
        var tools = Repository("tools");
        await catalog.AddAsync("tools", tools);
        var peers = Substitute.For<IPeerRepository>();
        peers.GetAsync(new NodeId("workstation-1"), Arg.Any<CancellationToken>())
            .Returns(new PeerDescriptor(new NodeId("workstation-1"), "Workstation", NodeRole.Workstation, "10.0.0.2", 22, "dev"));
        var asked = new List<string>();
        _git.FilterExistingCommitsAsync(Arg.Do<string>(asked.Add), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<string>());
        _git.CreateBundleAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>()).Returns(false);
        const string TOKEN = "catalog-tests-link-token-0123456789";
        var service = new GitBundleService(
            TestTexts.English, OriginNode(), peers, new LinkTokenProvider(new SyncBridgeOptions { LinkToken = TOKEN }, new InMemorySecretStore()), _git, _appData, catalog);
        var context = new PatchReceiveContext("workstation-1", TOKEN);

        await service.CreateAsync(Array.Empty<string>(), context, "tools");
        await service.CreateAsync(Array.Empty<string>(), context);
        var unknown = () => service.CreateAsync(Array.Empty<string>(), context, "../secret");

        asked.Should().Equal(tools, catalog.Current!.Path);
        (await unknown.Should().ThrowAsync<PatchRejectedException>()).Which.Reason.Should().Be(PatchRejectionReason.UnknownRepository);
        await _git.DidNotReceive().CreateBundleAsync(Arg.Is<string>(path => path.Contains("secret")), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies that the sync base metadata of each repository is kept apart.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Sync_Base_Metadata_Is_Kept_Per_Repository()
    {
        var (catalog, _) = CreateOrigin();
        await catalog.AddAsync("tools", Repository("tools"));
        var store = new SyncBaseMetadataStore(_appData, catalog);
        await store.WriteAsync(new SyncBaseMetadata(SyncBaseMode.Git, "main", new string('a', 40), new Dictionary<string, string?>()));

        await catalog.ActivateAsync("tools");
        (await store.ReadAsync()).Should().BeNull("the other repository has no sync base yet");
        await store.WriteAsync(new SyncBaseMetadata(SyncBaseMode.Git, "dev", new string('b', 40), new Dictionary<string, string?>()));

        await catalog.ActivateAsync("app");
        (await store.ReadAsync())!.Branch.Should().Be("main");
    }

    /// <summary>
    /// Creates the catalog of an Origin with one repository, stored in a configuration file.
    /// </summary>
    /// <param name="workingTreeLock">The lock of the working tree, or <see langword="null"/> for a new one.</param>
    /// <returns>The catalog and the path of the file.</returns>
    private (RepositoryCatalog Catalog, string ConfigPath) CreateOrigin(WorkingTreeLock? workingTreeLock = null)
    {
        var configPath = Path.Combine(_root, "syncbridge.json");
        var options = new SyncBridgeOptions
        {
            NodeId = "origin-1",
            Role = NodeRole.Origin,
            Repositories = { new RepositoryOptions { Name = "app", Path = Repository("app") } },
            ActiveRepository = "app"
        };
        File.WriteAllText(configPath, """{ "SyncBridge": { "NodeId": "origin-1", "Role": "Origin" } }""");
        var catalog = new RepositoryCatalog(
            TestTexts.English, SyncBridgeOptionsValidator.ToNodeConfig(options), options, workingTreeLock ?? new WorkingTreeLock(), _appData, new RepositoryCatalogStorage(configPath), _git);
        return (catalog, configPath);
    }

    /// <summary>
    /// Describes the Origin node of the tests.
    /// </summary>
    /// <returns>The node.</returns>
    private NodeConfig OriginNode() => new(new NodeId("origin-1"), "Origin", NodeRole.Origin, Path.Combine(_root, "app"), null);

    /// <summary>
    /// Creates a folder that looks like a Git repository.
    /// </summary>
    /// <param name="name">The folder name.</param>
    /// <returns>The path.</returns>
    private string Repository(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.Combine(path, ".git"));
        File.WriteAllText(Path.Combine(path, ".git", "HEAD"), "ref: refs/heads/main\n");
        File.WriteAllText(Path.Combine(path, "readme.md"), name);
        return path;
    }

    /// <summary>
    /// Lists the files of a folder with their content.
    /// </summary>
    /// <param name="directory">The folder.</param>
    /// <returns>The relative paths and the contents, in order.</returns>
    private static string[] Snapshot(string directory)
        => Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(file => Path.GetRelativePath(directory, file) + "=" + File.ReadAllText(file))
            .ToArray();

    /// <summary>
    /// Runs an operation that must be refused and returns the reason.
    /// </summary>
    /// <param name="operation">The operation.</param>
    /// <returns>The message of the refusal.</returns>
    private static async Task<string> RefusedAsync(Func<Task> operation)
    {
        var refused = await operation.Should().ThrowAsync<SyncBridgeException>();
        return TestTexts.English.Describe(refused.Which);
    }

    /// <summary>
    /// Wraps <see cref="RefusedAsync"/> so the reason can be asserted in one line.
    /// </summary>
    /// <param name="operation">The operation.</param>
    /// <returns>The assertion helper.</returns>
    private static Refusal Refused(Func<Task> operation) => new(RefusedAsync(operation));

    /// <summary>
    /// The reason of a refusal that is still being awaited.
    /// </summary>
    /// <param name="Reason">The task that gives the reason.</param>
    private sealed record Refusal(Task<string> Reason)
    {
        /// <summary>
        /// Returns the assertions over this refusal.
        /// </summary>
        /// <returns>This refusal.</returns>
        public Refusal Should() => this;

        /// <summary>
        /// Asserts that the reason contains a text.
        /// </summary>
        /// <param name="expected">The text.</param>
        /// <returns>A task representing the assertion.</returns>
        public async Task ContainAsync(string expected) => (await Reason).Should().Contain(expected);
    }
}
