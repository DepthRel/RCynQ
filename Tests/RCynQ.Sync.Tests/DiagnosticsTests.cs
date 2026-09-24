using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using RCynQ.Core.Abstractions.Events;
using RCynQ.Core.Abstractions.Processes;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Config;
using RCynQ.Core.Data.Storage;
using RCynQ.Core.Diagnostics;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Ids;
using RCynQ.Core.State;
using RCynQ.Git.Commands;
using RCynQ.Git.Hashing;
using RCynQ.Git.Manifest;
using RCynQ.Git.Processes;
using RCynQ.Ssh.Keys.AuthorizedKeys;
using RCynQ.Sync.Diagnostics.AutoFix;
using RCynQ.Sync.Diagnostics.Checks;
using RCynQ.Sync.Diagnostics.Endpoint;
using RCynQ.Sync.Diagnostics.Runner;
using RCynQ.Sync.Security;
using RCynQ.Sync.Tests.Fakes;
using RCynQ.Sync.Tests.Fixtures;
using RCynQ.Transports.Abstractions.Management;

namespace RCynQ.Sync.Tests;

/// <summary>
/// Tests for diagnostics checks, the runner and the safe auto-fixer.
/// </summary>
public sealed class DiagnosticsTests : IDisposable
{
    /// <summary>The fixture providing temporary directories.</summary>
    private readonly SyncFixture _fixture = new();

    /// <summary>
    /// Verifies role filtering, crash isolation, persistence and event publishing.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Runner_Filters_By_Role_And_Publishes()
    {
        var mediatorOnly = FakeCheck("MediatorOnly", NodeRole.Mediator);
        var everyone = FakeCheck("Everyone", NodeRole.Source, NodeRole.Mediator, NodeRole.Dest);
        var crashing = Substitute.For<IDiagnosticCheck>();
        crashing.Name.Returns("Crashing");
        crashing.Roles.Returns(new HashSet<NodeRole> { NodeRole.Mediator });
        crashing.RunAsync(Arg.Any<DiagnosticContext>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("boom"));
        var sourceOnly = FakeCheck("SourceOnly", NodeRole.Source);

        var events = Substitute.For<IEventPublisher>();
        var runs = Substitute.For<IDiagnosticRunRepository>();
        var chain = Substitute.For<IChainLinkRepository>();
        chain.GetAllAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<ChainLinkConfig>());
        var node = new NodeConfig(new NodeId("mediator-1"), "Relay", NodeRole.Mediator, null, null);
        var runner = new DiagnosticsRunner(
            new[] { mediatorOnly, everyone, crashing, sourceOnly }, node, new SyncBridgeOptions { NodeId = "mediator-1", Role = NodeRole.Mediator },
            chain, new AppDataFactory(AppDataPaths.FromRoot(Path.Combine(_fixture.Root, "mediator-data"))), events, TimeProvider.System, runs);

        runner.GetApplicableChecks(NodeRole.Source).Select(check => check.Name).Should().Equal("Everyone", "SourceOnly");
        var run = await runner.RunAsync();

        run.Role.Should().Be(NodeRole.Mediator);
        run.Results.Select(result => result.CheckName).Should().Equal("MediatorOnly", "Everyone", "Crashing");
        run.Results[2].Passed.Should().BeFalse();
        run.Results[2].Message.Should().Contain("boom");
        run.AllPassed.Should().BeFalse();
        await runs.Received(1).AddAsync(run, Arg.Any<CancellationToken>());
        await events.Received(1).PublishDiagnosticsAsync(run, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies git presence and version checks.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Git_Checks_Detect_Missing_And_Old_Git()
    {
        var processes = Substitute.For<IProcessRunner>();
        var context = Context(_fixture.DestNode);
        var git = new GitOptions();

        processes.RunAsync(Arg.Any<ProcessSpec>(), Arg.Any<CancellationToken>()).Returns(new ProcessResult(0, "git version 2.55.0.windows.5\n", ""));
        (await new GitInstalledCheck(processes, git).RunAsync(context)).Passed.Should().BeTrue();
        (await new GitVersionCheck(processes, git).RunAsync(context)).Passed.Should().BeTrue();

        processes.RunAsync(Arg.Any<ProcessSpec>(), Arg.Any<CancellationToken>()).Returns(new ProcessResult(0, "git version 2.20.1\n", ""));
        var old = await new GitVersionCheck(processes, git).RunAsync(context);
        old.Passed.Should().BeFalse();
        old.Message.Should().Contain("2.20");

        processes.RunAsync(Arg.Any<ProcessSpec>(), Arg.Any<CancellationToken>()).ThrowsAsync(new SyncBridgeException("not found"));
        (await new GitInstalledCheck(processes, git).RunAsync(context)).SuggestedFix.Should().Contain("Install Git");
        GitVersionCheck.ParseVersion("nonsense").Should().BeNull();
    }

    /// <summary>
    /// Verifies the working directory check and its proposed fix.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Workdir_Check_Proposes_Create_Directory()
    {
        var missing = Path.Combine(_fixture.Root, "missing-work");
        var node = new NodeConfig(new NodeId("source-1"), "Source", NodeRole.Source, null, missing);

        var failed = await new WorkdirWritableCheck().RunAsync(Context(node));
        failed.Passed.Should().BeFalse();
        failed.AutoFix!.Kind.Should().Be(DiagnosticFixKind.CreateDirectory);
        failed.AutoFix.Parameters["path"].Should().Be(missing);

        (await new WorkdirWritableCheck().RunAsync(Context(_fixture.SourceNode))).Passed.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that the auto-fixer refuses unsafe and unconfirmed fixes and executes safe confirmed ones.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task AutoFixer_Allows_Only_Safe_Confirmed_Fixes()
    {
        var links = Substitute.For<ILinkManager>();
        var linkId = Guid.NewGuid();
        links.StartAsync(linkId, Arg.Any<CancellationToken>()).Returns(new LinkState(linkId, "hop", TransportKind.AmneziaWG, LinkStatus.Ready, "up", DateTimeOffset.UtcNow));
        var node = _fixture.SourceNode;
        var appData = new AppDataFactory(AppDataPaths.FromRoot(Path.Combine(_fixture.Root, "source-data")));
        var fixer = new SafeAutoFixer(new IAutoFixHandler[]
        {
            new CreateDirectoryFixHandler(node, appData),
            new StartTransportFixHandler(links),
            new RestartLocalEndpointFixHandler(new ExternalLocalEndpointController())
        });

        foreach (var forbidden in new[] { DiagnosticFixKind.GlobalSshConfigChange, DiagnosticFixKind.DeleteUserData, DiagnosticFixKind.SystemServiceChange })
        {
            fixer.IsAllowed(forbidden).Should().BeFalse();
            (await fixer.ApplyAsync(new DiagnosticFix(forbidden, "dangerous"), confirmed: true)).Applied.Should().BeFalse();
        }

        var inside = Path.Combine(_fixture.SourceWork, "created", "nested");
        var createInside = new DiagnosticFix(DiagnosticFixKind.CreateDirectory, "create", new Dictionary<string, string> { ["path"] = inside });
        (await fixer.ApplyAsync(createInside, confirmed: false)).Message.Should().Contain("confirmation");
        Directory.Exists(inside).Should().BeFalse();

        (await fixer.ApplyAsync(createInside, confirmed: true)).Applied.Should().BeTrue();
        Directory.Exists(inside).Should().BeTrue();

        var outside = Path.Combine(Path.GetTempPath(), "rcynq-outside-" + Guid.NewGuid().ToString("N"));
        (await fixer.ApplyAsync(new DiagnosticFix(DiagnosticFixKind.CreateDirectory, "create", new Dictionary<string, string> { ["path"] = outside }), true)).Applied.Should().BeFalse();
        Directory.Exists(outside).Should().BeFalse();

        (await fixer.ApplyAsync(new DiagnosticFix(DiagnosticFixKind.StartTransport, "start", new Dictionary<string, string> { ["linkId"] = linkId.ToString() }), true)).Applied.Should().BeTrue();
        await links.Received(1).StartAsync(linkId, Arg.Any<CancellationToken>());

        (await fixer.ApplyAsync(new DiagnosticFix(DiagnosticFixKind.AddRestrictedAuthorizedKey, "add key"), true)).Message.Should().Contain("No handler");
        (await fixer.ApplyAsync(new DiagnosticFix(DiagnosticFixKind.RestartLocalEndpoint, "restart"), true)).Applied.Should().BeFalse();
    }

    /// <summary>
    /// Verifies chain configuration checks for missing signing keys and weak tokens.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task ChainLinkConfig_Check_Reports_Problems()
    {
        var options = new SyncBridgeOptions { NodeId = "source-1", Role = NodeRole.Source, WorkdirPath = _fixture.SourceWork, LinkToken = "short" };
        var check = new ChainLinkConfigCheck(new LinkTokenProvider(options, new InMemorySecretStore()));
        var chain = _fixture.CreateChain();

        var failed = await check.RunAsync(Context(_fixture.SourceNode, options, chain));
        failed.Passed.Should().BeFalse();
        failed.Message.Should().Contain("no signing public key").And.Contain("shorter than 16");

        options.LinkToken = SyncFixture.Token;
        var signed = chain.Select(link => link.Peer.Role != NodeRole.Dest ? link : new ChainLinkConfig(link.Id, link.Name, link.Transport,
            new PeerDescriptor(link.Peer.Id, link.Peer.Name, NodeRole.Dest, link.Peer.Host, 22, "rcynq", signingPublicKey: Convert.ToBase64String(new byte[] { 1 })), link.LocalPort, link.RemotePort, link.Priority)).ToArray();
        (await check.RunAsync(Context(_fixture.SourceNode, options, signed))).Passed.Should().BeTrue();
    }

    /// <summary>
    /// Verifies snapshot integrity outcomes.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task SnapshotIntegrity_Check_Compares_Hashes()
    {
        var manifest = new DefaultFileManifestBuilder(new SnapshotFilter());
        var hasher = new DefaultStateHasher();
        var state = Substitute.For<INodeStateStore>();
        var check = new SnapshotIntegrityCheck(manifest, hasher, state, _fixture.DestSyncBase, _fixture.DestLock);
        var paths = AppDataPaths.FromRoot(Path.Combine(_fixture.Root, "dest-data"));
        var context = new DiagnosticContext(_fixture.DestNode, new SyncBridgeOptions(), Array.Empty<ChainLinkConfig>(), paths);
        var actual = hasher.ComputeHash(await manifest.ScanAsync(paths.SyncBaseDirectory, _fixture.DestWork));

        state.GetAsync(Arg.Any<CancellationToken>()).Returns(new SyncState(null, actual, null, null));
        (await check.RunAsync(context)).Passed.Should().BeTrue();

        state.GetAsync(Arg.Any<CancellationToken>()).Returns(new SyncState(null, new string('0', 64), null, null));
        (await check.RunAsync(context)).Message.Should().Contain("does not match");

        Directory.Delete(paths.SyncBaseDirectory, recursive: true);
        var missing = await check.RunAsync(context);
        missing.Passed.Should().BeFalse("a working tree without Git and without a manual baseline has no sync base");
        missing.SuggestedFix.Should().Contain("baseline");
    }

    /// <summary>
    /// Verifies that a Source whose working tree is a Git repository pulled from Dest passes: its sync base is built from the
    /// commit automatically, instead of the user being told to create a baseline.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task SnapshotIntegrity_Check_Passes_A_Git_Native_Source_Without_A_Snapshot()
    {
        var probe = await new SystemProcessRunner().RunAsync(new ProcessSpec("git", new[] { "--version" }, null, null, TimeSpan.FromSeconds(30)));
        if (probe.ExitCode != 0)
        {
            return;
        }

        var git = new GitCommandRunner(new SystemProcessRunner(), new GitOptions());
        await git.RunAsync(new[] { "init", "--quiet", "--initial-branch=main" }, _fixture.SourceWork);
        await git.RunAsync(new[] { "add", "-A" }, _fixture.SourceWork);
        await git.RunAsync(new[] { "-c", "user.name=t", "-c", "user.email=t@t", "commit", "--quiet", "-m", "first" }, _fixture.SourceWork);
        await git.RunAsync(new[] { "update-ref", "refs/remotes/dest/main", "HEAD" }, _fixture.SourceWork);

        var check = new SnapshotIntegrityCheck(new DefaultFileManifestBuilder(new SnapshotFilter()), new DefaultStateHasher(), Substitute.For<INodeStateStore>(), _fixture.SourceSyncBase, _fixture.SourceLock);
        var paths = AppDataPaths.FromRoot(Path.Combine(_fixture.Root, "source-data"));
        var context = new DiagnosticContext(_fixture.SourceNode, new SyncBridgeOptions(), Array.Empty<ChainLinkConfig>(), paths);

        var result = await check.RunAsync(context);

        result.Passed.Should().BeTrue(result.Message);
        result.Message.Should().Contain("built from Git commit").And.Contain("main");
        SyncFixture.Read(paths.SyncBaseDirectory, "a.txt").Should().Be("v1\n", "the sync base is exported from the commit");
    }

    /// <summary>
    /// Verifies that unrestricted RCynQ keys are reported.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task AuthorizedKeys_Check_Detects_Unrestricted_RCynQ_Keys()
    {
        var path = Path.Combine(_fixture.Root, "authorized_keys");
        const string Blob = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIE2mfktackE6lnW8Hp/QiIkF7Dp5hFWxNzR5c9wXgKja";
        await File.WriteAllTextAsync(path, $"{Blob} admin@laptop\n{Blob} rcynq:PatchForward\n");
        var check = new AuthorizedKeysFormatCheck(new AuthorizedKeysManager(new SshClientOptions { AuthorizedKeysPath = path }));

        var result = await check.RunAsync(Context(_fixture.DestNode));

        result.Passed.Should().BeFalse();
        result.Message.Should().Contain("lines: 2");
    }

    /// <summary>
    /// Deletes the fixture.
    /// </summary>
    public void Dispose() => _fixture.Dispose();

    /// <summary>
    /// Creates a passing fake check.
    /// </summary>
    /// <param name="name">The check name.</param>
    /// <param name="roles">The roles.</param>
    /// <returns>The check.</returns>
    private static IDiagnosticCheck FakeCheck(string name, params NodeRole[] roles)
    {
        var check = Substitute.For<IDiagnosticCheck>();
        check.Name.Returns(name);
        check.Roles.Returns(roles.ToHashSet());
        check.RunAsync(Arg.Any<DiagnosticContext>(), Arg.Any<CancellationToken>()).Returns(DiagnosticResult.Pass(name));
        return check;
    }

    /// <summary>
    /// Creates a diagnostic context.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <param name="options">The options.</param>
    /// <param name="chain">The chain.</param>
    /// <returns>The context.</returns>
    private DiagnosticContext Context(NodeConfig node, SyncBridgeOptions? options = null, IReadOnlyList<ChainLinkConfig>? chain = null)
        => new(node, options ?? new SyncBridgeOptions(), chain ?? Array.Empty<ChainLinkConfig>(), AppDataPaths.FromRoot(Path.Combine(_fixture.Root, "ctx-data")));
}
