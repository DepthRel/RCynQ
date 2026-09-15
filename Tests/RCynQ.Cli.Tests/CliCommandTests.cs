using System.Security.Cryptography;
using CliFx.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using RCynQ.Cli.Hosting;
using RCynQ.Cli.Tests.Support;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Abstractions.Security;
using RCynQ.Core.Config;
using RCynQ.Core.Diagnostics;
using RCynQ.Core.Enums;
using RCynQ.Core.Events;
using RCynQ.Core.Ids;
using RCynQ.Core.Patches;
using RCynQ.Core.State;
using RCynQ.Ssh.Keys.AuthorizedKeys;
using RCynQ.Sync.Configuration;
using RCynQ.Sync.Configuration.Access;
using RCynQ.Sync.Services;
using RCynQ.Transports.Abstractions.Management;
using Xunit;

namespace RCynQ.Cli.Tests;

/// <summary>
/// Tests for command parsing, configuration handling, output formatting and export/import through the CLI.
/// </summary>
public sealed class CliCommandTests : IDisposable
{
    /// <summary>A temporary directory.</summary>
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "rcynq-cli-tests", Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Initializes a new instance of the <see cref="CliCommandTests"/> class.
    /// </summary>
    public CliCommandTests()
    {
        Directory.CreateDirectory(_temp);
    }

    /// <summary>
    /// Verifies that <c>role set</c> creates and edits the configuration file and validates role paths.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task RoleSet_Writes_Config_And_Validates_Paths()
    {
        var config = Path.Combine(_temp, "syncbridge.json");

        var (code, output, _) = await RunAsync(null, "role", "set", "dest", "--repo", _temp, "--node-id", "dest-1", "--config", config);
        code.Should().Be(0, output);
        var options = await ConfigurationFileEditor.LoadAsync(config);
        options.Role.Should().Be(NodeRole.Dest);
        options.NodeId.Should().Be("dest-1");
        options.RepoPath.Should().Be(_temp);

        (await RunAsync(null, "config", "validate", "--config", config)).Output.Should().Contain("is valid");

        (await RunAsync(null, "role", "set", "mediator", "--config", config)).Code.Should().Be(0);
        (await ConfigurationFileEditor.LoadAsync(config)).RepoPath.Should().BeNull();

        var (sourceCode, _, error) = await RunAsync(null, "role", "set", "source", "--config", config);
        sourceCode.Should().Be(2);
        error.Should().Contain("working directory");
    }

    /// <summary>
    /// Verifies that validation reports every problem with exit code 2.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task ConfigValidate_Reports_Errors()
    {
        var config = Path.Combine(_temp, "bad.json");
        await File.WriteAllTextAsync(config, """{ "SyncBridge": { "NodeId": "bad id", "Role": "Source", "ListenPort": 70000 } }""");

        var (code, _, error) = await RunAsync(null, "config", "validate", "--config", config);

        code.Should().Be(2);
        error.Should().Contain("Node:").And.Contain("ListenPort");
    }

    /// <summary>
    /// Verifies the status report.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Status_Formats_Node_Links_And_Patches()
    {
        var hash = new string('b', 64);
        var patches = Substitute.For<IPatchService>();
        patches.GetStateAsync(Arg.Any<CancellationToken>()).Returns(new NodeSyncStatus(
            new NodeId("source-1"), "Laptop", NodeRole.Source, new SyncState(hash, hash, DateTimeOffset.UtcNow, null), true,
            new[] { new FileChange(ChangeKind.Modified, "src/a.cs", hash, 3, false) }, 1));
        patches.GetRecentAsync(5, Arg.Any<CancellationToken>()).Returns(new[]
        {
            new PatchMetadata(Guid.NewGuid(), PatchDirection.Outgoing, new NodeId("source-1"), new NodeId("dest-1"), DateTimeOffset.UtcNow, hash, hash, PatchStatus.Applied, DateTimeOffset.UtcNow, Array.Empty<FileChange>())
        });
        var links = Substitute.For<IChainLinkRepository>();
        links.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new[] { Relay() });

        var (code, output, _) = await RunAsync(services => services.AddSingleton(patches).AddSingleton(links), "status");

        code.Should().Be(0);
        output.Should().Contain("source-1 (Source) Laptop")
            .And.Contain("Baseline:       initialized")
            .And.Contain("M src/a.cs")
            .And.Contain("patch resolve")
            .And.Contain("relay")
            .And.Contain("jump@203.0.113.4:22")
            .And.Contain("Recent patches");
    }

    /// <summary>
    /// Verifies parsing of <c>patch resolve</c>.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task PatchResolve_Parses_Arguments()
    {
        var id = Guid.NewGuid();
        var patches = Substitute.For<IPatchService>();
        patches.ResolveAsync(id, ConflictPolicy.UseGit, Arg.Any<CancellationToken>())
            .Returns(new PatchApplyResult(id, PatchStatus.ConflictResolved, 2, Array.Empty<ConflictFile>(), null));
        void Register(IServiceCollection services) => services.AddSingleton(patches);

        var (code, output, _) = await RunAsync(Register, "patch", "resolve", id.ToString(), "--policy", "UseGit");
        code.Should().Be(0);
        output.Should().Contain("ConflictResolved");

        (await RunAsync(Register, "patch", "resolve", id.ToString())).Code.Should().NotBe(0, "the policy is required");
        (await RunAsync(Register, "patch", "resolve", id.ToString(), "--policy", "Whatever")).Code.Should().NotBe(0);
        (await RunAsync(Register, "patch", "resolve", "not-a-guid", "--policy", "KeepLocal")).Code.Should().NotBe(0);
        await patches.Received(1).ResolveAsync(Arg.Any<Guid>(), Arg.Any<ConflictPolicy>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies that transports are addressed by link name.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task TransportUp_Resolves_Link_By_Name()
    {
        var relay = Relay();
        var links = Substitute.For<IChainLinkRepository>();
        links.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new[] { relay });
        var manager = Substitute.For<ILinkManager>();
        manager.StartAsync(relay.Id, Arg.Any<CancellationToken>()).Returns(new LinkState(relay.Id, relay.Name, TransportKind.Direct, LinkStatus.Degraded, "fallback", DateTimeOffset.UtcNow));
        void Register(IServiceCollection services) => services.AddSingleton(links).AddSingleton(manager);

        var (code, output, _) = await RunAsync(Register, "transport", "up", "relay");
        code.Should().Be(0);
        output.Should().Contain("relay: Degraded via Direct");

        var (unknownCode, _, error) = await RunAsync(Register, "transport", "up", "nope");
        unknownCode.Should().Be(2);
        error.Should().Contain("No link");
    }

    /// <summary>
    /// Verifies export and import validation through the commands.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Export_And_Import_Validate_Files()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var signatures = Substitute.For<ISignatureService>();
        signatures.ExportPublicKeyAsync(Arg.Any<CancellationToken>()).Returns(key.ExportSubjectPublicKeyInfo());
        var peers = Substitute.For<IPeerRepository>();
        var links = Substitute.For<IChainLinkRepository>();
        links.GetAllAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<ChainLinkConfig>());
        void Register(IServiceCollection services, NodeConfig node) => services
            .AddSingleton(node).AddSingleton(new SyncBridgeOptions { ListenPort = 18443 }).AddSingleton(signatures)
            .AddSingleton(peers).AddSingleton(links).AddSingleton(TimeProvider.System).AddSingleton<NodeConfigurationTransfer>().AddSingleton(Substitute.For<RCynQ.Sync.Configuration.Probing.INodeEnvironmentProbe>());

        var exportFile = Path.Combine(_temp, "dest.json");
        var dest = new NodeConfig(new NodeId("dest-1"), "Dest", NodeRole.Dest, "/srv/repo", null);
        var (exportCode, exportOutput, _) = await RunAsync(services => Register(services, dest), "export", "--host", "10.8.0.2", "--user", "rcynq", "--output", exportFile);
        exportCode.Should().Be(0, exportOutput);
        File.ReadAllText(exportFile).Should().Contain("\"signingPublicKey\"").And.NotContain("PRIVATE");

        var source = new NodeConfig(new NodeId("source-1"), "Source", NodeRole.Source, null, _temp);
        var (importCode, importOutput, _) = await RunAsync(services => Register(services, source), "import", exportFile);
        importCode.Should().Be(0, importOutput);
        importOutput.Should().Contain("added link 'dest-1'");
        await peers.Received(1).UpsertAsync(Arg.Is<PeerDescriptor>(peer => peer.Id == new NodeId("dest-1") && peer.GitRepoPath == "/srv/repo"), Arg.Any<CancellationToken>());
        await links.Received(1).UpsertAsync(Arg.Is<ChainLinkConfig>(link => link.RemotePort == 18443 && link.Peer.Role == NodeRole.Dest), Arg.Any<CancellationToken>());

        var garbage = Path.Combine(_temp, "garbage.json");
        await File.WriteAllTextAsync(garbage, "{ \"formatVersion\": 99 }");
        var (garbageCode, _, garbageError) = await RunAsync(services => Register(services, source), "import", garbage);
        garbageCode.Should().Be(2);
        garbageError.Should().NotBeEmpty();
    }

    /// <summary>
    /// Verifies that <c>ssh allow</c> writes the neighbour keys and fails without administrator rights.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task SshAllow_Writes_Neighbour_Keys()
    {
        var keysFile = Path.Combine(_temp, "authorized_keys");
        var peers = Substitute.For<IPeerRepository>();
        peers.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new[]
        {
            new PeerDescriptor(new NodeId("source-1"), "Source", NodeRole.Source, "10.8.1.3", 22, "dev", sshPublicKey: "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIOMqqnkVzrm0SdG6UOoqKLsabgH5C9okWi0dh2l9GKJl rcynq:source-1")
        });
        var dest = new NodeConfig(new NodeId("dest-1"), "Dest", NodeRole.Dest, "/srv/repo", null);
        void Register(IServiceCollection services) => services
            .AddSingleton(new PeerAccessService(dest, new SyncBridgeOptions { ListenPort = 18443 }, peers, new AuthorizedKeysManager(new SshClientOptions { AuthorizedKeysPath = keysFile })));

        var (code, output, _) = await RunAsync(Register, "ssh", "allow");

        code.Should().Be(0, output);
        output.Should().Contain("Allowed 1 neighbour key(s)");
        File.ReadAllText(keysFile).Should().Contain("permitopen=\"127.0.0.1:18443\"").And.Contain("rcynq:source-1");

        void RegisterAdmin(IServiceCollection services) => services
            .AddSingleton(new PeerAccessService(dest, new SyncBridgeOptions(), peers, new AuthorizedKeysManager(new AuthorizedKeysTarget(Path.Combine(_temp, "admin_keys"), true), () => false)));
        var (adminCode, _, adminError) = await RunAsync(RegisterAdmin, "ssh", "allow");
        adminCode.Should().Be(1);
        adminError.Should().Contain("Administrator rights");
    }

    /// <summary>
    /// Verifies diagnostics output, failure exit code and confirmed fixes.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Diagnostics_Prints_Fixes_And_Applies_Confirmed()
    {
        var fix = new DiagnosticFix(DiagnosticFixKind.CreateDirectory, "Create '/work'.", new Dictionary<string, string> { ["path"] = "/work" });
        var runner = Substitute.For<IDiagnosticsRunner>();
        runner.RunAsync(Arg.Any<CancellationToken>()).Returns(new DiagnosticsEvent(NodeRole.Source, new[]
        {
            DiagnosticResult.Pass("GitInstalled", "git version 2.55"),
            DiagnosticResult.Fail("WorkdirWritable", "missing", "Create the directory.", fix)
        }, DateTimeOffset.UtcNow));
        var fixer = Substitute.For<IAutoFixer>();
        fixer.ApplyAsync(fix, true, Arg.Any<CancellationToken>()).Returns(new AutoFixResult(true, "Created '/work'."));
        void Register(IServiceCollection services) => services.AddSingleton(runner).AddSingleton(fixer);

        var (code, output, _) = await RunAsync(Register, "diagnostics", "run");
        code.Should().Be(1);
        output.Should().Contain("[ OK ] GitInstalled").And.Contain("[FAIL] WorkdirWritable").And.Contain("auto-fix available").And.Contain("1 passed, 1 failed");
        await fixer.DidNotReceiveWithAnyArgs().ApplyAsync(default!, default, default);

        (await RunAsync(Register, "diagnostics", "run", "--fix", "--yes")).Output.Should().Contain("auto-fix applied: Created '/work'.");
        await fixer.Received(1).ApplyAsync(fix, true, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Deletes the temporary directory.
    /// </summary>
    public void Dispose()
    {
        try
        {
            Directory.Delete(_temp, recursive: true);
        }
        catch (IOException)
        {
            // Best effort cleanup.
        }
    }

    /// <summary>
    /// Runs the CLI with an in-memory console.
    /// </summary>
    /// <param name="register">Test node registrations, or <see langword="null"/> for commands without node context.</param>
    /// <param name="args">The arguments.</param>
    /// <returns>The exit code, standard output and standard error.</returns>
    private static async Task<(int Code, string Output, string Error)> RunAsync(Action<IServiceCollection>? register, params string[] args)
    {
        using var console = new FakeInMemoryConsole();
        var factory = new TestNodeContextFactory(register ?? (_ => { }));
        var code = await CliHost.CreateApplication(factory, console).RunAsync(args, new Dictionary<string, string>());
        return (code, console.ReadOutputString(), console.ReadErrorString());
    }

    /// <summary>
    /// Creates a mediator link.
    /// </summary>
    /// <returns>The link.</returns>
    private static ChainLinkConfig Relay()
        => new(Guid.NewGuid(), "relay", TransportKind.AmneziaWG, new PeerDescriptor(new NodeId("mediator-1"), "Relay", NodeRole.Mediator, "203.0.113.4", 22, "jump"), 0, 22, 10, new[] { TransportKind.Direct });
}
