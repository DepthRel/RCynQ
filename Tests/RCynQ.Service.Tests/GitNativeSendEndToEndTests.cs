using System.Globalization;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Abstractions.Ssh;
using RCynQ.Core.Abstractions.Storage;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Ids;
using RCynQ.Git.Commands;
using RCynQ.Git.Processes;
using RCynQ.Service.Tests.Support;
using RCynQ.Sync.Endpoint;
using RCynQ.Sync.GitChannel;
using RCynQ.Sync.Services;

namespace RCynQ.Service.Tests;

/// <summary>
/// End-to-end tests proving the point of the Git-native pending-changes redesign: a Workstation node that is a Git working tree
/// and has pulled from Origin can send its local edits straight away, without ever creating a sync-base snapshot. Two real
/// services (Workstation and Origin) run on Kestrel with real Git repositories; only the SSH forward is replaced by a direct
/// connection to Origin's loopback port. They return early when <c>git</c> is not installed.
/// </summary>
public sealed class GitNativeSendEndToEndTests : IAsyncLifetime
{
    /// <summary>The Origin service.</summary>
    private ServiceTestHost? _origin;

    /// <summary>The Workstation service.</summary>
    private ServiceTestHost? _workstation;

    /// <summary>The Git repository of Origin.</summary>
    private string _originWork = null!;

    /// <summary>The working tree of Workstation.</summary>
    private string _workstationWork = null!;

    /// <summary>The chain repository substitute of Workstation, so its target can be set once Origin's port and fingerprint are known.</summary>
    private readonly IChainLinkRepository _chain = Substitute.For<IChainLinkRepository>();

    /// <summary>The forward manager substitute of Workstation, replaced by a direct connection to Origin's loopback port.</summary>
    private readonly ILocalForwardManager _forwards = Substitute.For<ILocalForwardManager>();

    /// <summary>The Git command runner used to prepare Origin's repository.</summary>
    private readonly GitCommandRunner _git = new(new SystemProcessRunner(TestTexts.English), new GitOptions());

    /// <summary>
    /// Starts Origin with a repository and Workstation pointed at it through a direct TLS connection.
    /// </summary>
    /// <returns>A task representing the setup.</returns>
    public async Task InitializeAsync()
    {
        if (!await GitAvailableAsync())
        {
            return;
        }

        var tlsPort = ServiceTestHost.FreePort();
        _origin = await ServiceTestHost.StartAsync(
            NodeRole.Origin,
            extraSettings: new Dictionary<string, string?>
            {
                ["SyncBridge:Endpoint:Mode"] = "Direct",
                ["SyncBridge:Endpoint:BindAddress"] = "127.0.0.1",
                ["SyncBridge:Endpoint:Port"] = tlsPort.ToString(CultureInfo.InvariantCulture)
            });
        _originWork = Path.Combine(_origin.Root, "work");
        await RunGitAsync(_originWork, "init", "--quiet", "--initial-branch=main");
        Write(_originWork, "readme.txt", "first\n");
        await RunGitAsync(_originWork, "add", "-A");
        await RunGitAsync(_originWork, "-c", "user.name=tester", "-c", "user.email=tester@example.com", "commit", "--quiet", "-m", "first commit");

        _workstation = await ServiceTestHost.StartAsync(
            NodeRole.Workstation,
            configureServices: services =>
            {
                services.AddSingleton(_chain);
                services.AddSingleton(_forwards);
            });
        _workstationWork = Path.Combine(_workstation.Root, "work");

        using (var scope = _origin.Services.CreateScope())
        {
            var workstationSignature = _workstation.Services.GetRequiredService<Core.Abstractions.Security.ISignatureService>();
            var key = Convert.ToBase64String(await workstationSignature.ExportPublicKeyAsync());
            await scope.ServiceProvider.GetRequiredService<IPeerRepository>()
                .UpsertAsync(new PeerDescriptor(new NodeId("workstation-1"), "Workstation", NodeRole.Workstation, "workstation.local", 22, "rcynq", signingPublicKey: key));
        }

        var fingerprint = FingerprintOfOrigin();
        _chain.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new[]
        {
            new ChainLinkConfig(
                Guid.NewGuid(), "origin", TransportKind.Direct,
                new PeerDescriptor(new NodeId("origin-1"), "Origin", NodeRole.Origin, "127.0.0.1", 22, "rcynq", endpointMode: EndpointMode.Direct, tlsFingerprint: fingerprint, endpointPort: tlsPort),
                0, tlsPort, 1000)
        });
        _forwards.StartAsync(Arg.Any<SshEndpoint>(), Arg.Any<IReadOnlyList<SshEndpoint>>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(tlsPort);
    }

    /// <summary>
    /// Stops both services.
    /// </summary>
    /// <returns>A task representing the cleanup.</returns>
    public async Task DisposeAsync()
    {
        if (_workstation is not null)
        {
            await _workstation.DisposeAsync();
        }

        if (_origin is not null)
        {
            await _origin.DisposeAsync();
        }
    }


    /// <summary>
    /// Verifies the whole Git cycle without any manual baseline on either node: Workstation clones Origin by Pull, sends an edit,
    /// Origin applies it and does not offer it back, Origin commits it together with its own edit, and the next Pull on Workstation
    /// goes through even though the sent file is still modified on Workstation, leaving nothing pending on either side.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Workstation_And_Origin_Sync_Through_Git_Without_A_Baseline()
    {
        if (_origin is null || _workstation is null)
        {
            return;
        }

        using var scope = _workstation.Services.CreateScope();
        var channel = scope.ServiceProvider.GetRequiredService<GitChannelService>();
        var patches = scope.ServiceProvider.GetRequiredService<IPatchService>();
        var originPatches = _origin.Services.GetRequiredService<IPatchService>();

        var originBefore = await originPatches.GetStateAsync();
        originBefore.SyncBase.Mode.Should().Be(SyncBaseMode.Git, originBefore.SyncBase.Problem);
        originBefore.PendingChanges.Should().BeEmpty();

        var pull = await channel.PullAsync();
        pull.Succeeded.Should().BeTrue(pull.Output);
        Read(_workstationWork, "readme.txt").Should().Be("first\n");

        var readyAfterPull = await patches.GetStateAsync();
        readyAfterPull.SyncBase.Mode.Should().Be(SyncBaseMode.Git, readyAfterPull.SyncBase.Problem);
        readyAfterPull.PendingChanges.Should().BeEmpty("nothing changed since the pull");

        Write(_workstationWork, "readme.txt", "edited on Workstation\n");
        (await patches.GetStateAsync()).PendingChanges.Select(change => change.Path).Should().Equal("readme.txt");

        var send = await patches.SendAsync();

        send.Sent.Should().BeTrue(send.Message);
        send.Result!.Status.Should().Be(PatchStatus.Applied, send.Result.Error);
        Read(_originWork, "readme.txt").Should().Be("edited on Workstation\n");
        (await patches.GetStateAsync()).PendingChanges.Should().BeEmpty("Origin has the edit, even though it did not commit it yet");

        // Origin offers only its own edit, not the one it received from Workstation.
        Write(_originWork, "origin-notes.txt", "typed on Origin\n");
        (await originPatches.GetStateAsync()).PendingChanges.Select(change => change.Path).Should().Equal("origin-notes.txt");

        await RunGitAsync(_originWork, "add", "-A");
        await RunGitAsync(_originWork, "-c", "user.name=tester", "-c", "user.email=tester@example.com", "commit", "--quiet", "-m", "commit what Workstation sent");
        (await originPatches.GetStateAsync()).PendingChanges.Should().BeEmpty("after the commit Workstation gets origin-notes.txt by Pull");

        // readme.txt is still modified on Workstation, but Origin has exactly that version: Pull replaces it instead of refusing.
        var secondPull = await channel.PullAsync();
        secondPull.Succeeded.Should().BeTrue(secondPull.Output);
        Read(_workstationWork, "origin-notes.txt").Should().Be("typed on Origin\n");
        Read(_workstationWork, "readme.txt").Should().Be("edited on Workstation\n");

        var afterSecondPull = await patches.GetStateAsync();
        afterSecondPull.PendingChanges.Should().BeEmpty("both nodes are on the same commit and nothing else changed");
        afterSecondPull.SyncBase.Commit.Should().Be((await RunGitOutputAsync(_originWork, "rev-parse", "HEAD")).Trim());
    }

    /// <summary>
    /// Verifies that Origin detects a conflict when Workstation changes a file that Origin has changed and committed on its own since the
    /// commit Workstation is built on, instead of silently overwriting Origin's version.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Origin_Commit_Of_Its_Own_Edit_Is_Not_Overwritten_By_Workstation()
    {
        if (_origin is null || _workstation is null)
        {
            return;
        }

        using var scope = _workstation.Services.CreateScope();
        var channel = scope.ServiceProvider.GetRequiredService<GitChannelService>();
        var patches = scope.ServiceProvider.GetRequiredService<IPatchService>();
        (await channel.PullAsync()).Succeeded.Should().BeTrue();

        Write(_originWork, "readme.txt", "edited and committed on Origin\n");
        await RunGitAsync(_originWork, "-c", "user.name=tester", "-c", "user.email=tester@example.com", "commit", "--quiet", "-am", "origin edit");
        Write(_workstationWork, "readme.txt", "edited on Workstation\n");

        var send = await patches.SendAsync();

        send.Result!.Status.Should().Be(PatchStatus.ConflictPending, send.Result.Error);
        Read(_originWork, "readme.txt").Should().Be("edited and committed on Origin\n");
    }

    /// <summary>
    /// Reads the certificate fingerprint that Origin published in its export file.
    /// </summary>
    /// <returns>The fingerprint.</returns>
    private string FingerprintOfOrigin()
    {
        using var certificate = NodeCertificateStore.GetOrCreate(_origin!.Services.GetRequiredService<IAppDataFactory>().Create(), "origin-1");
        return NodeCertificateStore.Fingerprint(certificate);
    }

    /// <summary>
    /// Runs git and asserts success.
    /// </summary>
    /// <param name="directory">The working directory.</param>
    /// <param name="arguments">The arguments.</param>
    /// <returns>A task representing the operation.</returns>
    private async Task RunGitAsync(string directory, params string[] arguments)
    {
        var result = await _git.RunAsync(arguments, directory);
        result.ExitCode.Should().Be(0, $"git {string.Join(' ', arguments)}: {result.StandardError}");
    }

    /// <summary>
    /// Runs git, asserts success and returns its output.
    /// </summary>
    /// <param name="directory">The working directory.</param>
    /// <param name="arguments">The arguments.</param>
    /// <returns>The standard output.</returns>
    private async Task<string> RunGitOutputAsync(string directory, params string[] arguments)
    {
        var result = await _git.RunAsync(arguments, directory);
        result.ExitCode.Should().Be(0, $"git {string.Join(' ', arguments)}: {result.StandardError}");
        return result.StandardOutput;
    }

    /// <summary>
    /// Checks whether Git is installed.
    /// </summary>
    /// <returns><see langword="true"/> when <c>git --version</c> works.</returns>
    private static async Task<bool> GitAvailableAsync()
    {
        try
        {
            return (await new SystemProcessRunner(TestTexts.English).RunAsync(new RCynQ.Core.Abstractions.Processes.ProcessSpec("git", new[] { "--version" }, null, null, TimeSpan.FromSeconds(30)))).ExitCode == 0;
        }
        catch (RCynQ.Core.Exceptions.SyncBridgeException)
        {
            return false;
        }
    }

    /// <summary>
    /// Writes a text file below a directory.
    /// </summary>
    /// <param name="directory">The directory.</param>
    /// <param name="relative">The relative path.</param>
    /// <param name="content">The content.</param>
    private static void Write(string directory, string relative, string content)
    {
        var full = Path.Combine(directory, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    /// <summary>
    /// Reads a text file below a directory.
    /// </summary>
    /// <param name="directory">The directory.</param>
    /// <param name="relative">The relative path.</param>
    /// <returns>The content.</returns>
    private static string Read(string directory, string relative)
        => File.ReadAllText(Path.Combine(directory, relative)).Replace("\r\n", "\n", StringComparison.Ordinal);
}
