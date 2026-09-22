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
/// End-to-end tests proving the point of the Git-native pending-changes redesign: a Source node that is a Git working tree
/// and has pulled from Dest can send its local edits straight away, without ever creating a sync-base snapshot. Two real
/// services (Source and Dest) run on Kestrel with real Git repositories; only the SSH forward is replaced by a direct
/// connection to Dest's loopback port. They return early when <c>git</c> is not installed.
/// </summary>
public sealed class GitNativeSendEndToEndTests : IAsyncLifetime
{
    /// <summary>The Dest service.</summary>
    private ServiceTestHost? _dest;

    /// <summary>The Source service.</summary>
    private ServiceTestHost? _source;

    /// <summary>The Git repository of Dest.</summary>
    private string _destWork = null!;

    /// <summary>The working tree of Source.</summary>
    private string _sourceWork = null!;

    /// <summary>The chain repository substitute of Source, so its target can be set once Dest's port and fingerprint are known.</summary>
    private readonly IChainLinkRepository _chain = Substitute.For<IChainLinkRepository>();

    /// <summary>The forward manager substitute of Source, replaced by a direct connection to Dest's loopback port.</summary>
    private readonly ILocalForwardManager _forwards = Substitute.For<ILocalForwardManager>();

    /// <summary>The Git command runner used to prepare Dest's repository.</summary>
    private readonly GitCommandRunner _git = new(new SystemProcessRunner(), new GitOptions());

    /// <summary>
    /// Starts Dest with a repository and Source pointed at it through a direct TLS connection.
    /// </summary>
    /// <returns>A task representing the setup.</returns>
    public async Task InitializeAsync()
    {
        if (!await GitAvailableAsync())
        {
            return;
        }

        var tlsPort = ServiceTestHost.FreePort();
        _dest = await ServiceTestHost.StartAsync(
            NodeRole.Dest,
            extraSettings: new Dictionary<string, string?>
            {
                ["SyncBridge:Endpoint:Mode"] = "Direct",
                ["SyncBridge:Endpoint:BindAddress"] = "127.0.0.1",
                ["SyncBridge:Endpoint:Port"] = tlsPort.ToString(CultureInfo.InvariantCulture)
            });
        _destWork = Path.Combine(_dest.Root, "work");
        await RunGitAsync(_destWork, "init", "--quiet", "--initial-branch=main");
        Write(_destWork, "readme.txt", "first\n");
        await RunGitAsync(_destWork, "add", "-A");
        await RunGitAsync(_destWork, "-c", "user.name=tester", "-c", "user.email=tester@example.com", "commit", "--quiet", "-m", "first commit");

        // Dest is not Git-native (out of scope for this redesign, see the plan): it still needs its own baseline to accept
        // an incoming patch, unrelated to whether Source needed one.
        await _dest.Services.GetRequiredService<IPatchService>().InitializeBaselineAsync();

        _source = await ServiceTestHost.StartAsync(
            NodeRole.Source,
            configureServices: services =>
            {
                services.AddSingleton(_chain);
                services.AddSingleton(_forwards);
            });
        _sourceWork = Path.Combine(_source.Root, "work");

        using (var scope = _dest.Services.CreateScope())
        {
            var sourceSignature = _source.Services.GetRequiredService<Core.Abstractions.Security.ISignatureService>();
            var key = Convert.ToBase64String(await sourceSignature.ExportPublicKeyAsync());
            await scope.ServiceProvider.GetRequiredService<IPeerRepository>()
                .UpsertAsync(new PeerDescriptor(new NodeId("source-1"), "Source", NodeRole.Source, "source.local", 22, "rcynq", signingPublicKey: key));
        }

        var fingerprint = FingerprintOfDest();
        _chain.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new[]
        {
            new ChainLinkConfig(
                Guid.NewGuid(), "dest", TransportKind.Direct,
                new PeerDescriptor(new NodeId("dest-1"), "Dest", NodeRole.Dest, "127.0.0.1", 22, "rcynq", endpointMode: EndpointMode.Direct, tlsFingerprint: fingerprint, endpointPort: tlsPort),
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
        if (_source is not null)
        {
            await _source.DisposeAsync();
        }

        if (_dest is not null)
        {
            await _dest.DisposeAsync();
        }
    }

    /// <summary>
    /// Verifies that once Source has pulled from Dest, it reports ready to send and can send a local edit straight away,
    /// without a sync-base snapshot ever existing on disk — the bug class this redesign eliminates. Also verifies the
    /// resulting behavior once Dest commits what it received: the file stops being pending only after Source pulls that
    /// commit, since the patch channel itself never commits on Dest.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Source_Sends_After_Pull_Without_Ever_Creating_A_Baseline()
    {
        if (_dest is null || _source is null)
        {
            return;
        }

        using var scope = _source.Services.CreateScope();
        var channel = scope.ServiceProvider.GetRequiredService<GitChannelService>();
        var patches = scope.ServiceProvider.GetRequiredService<IPatchService>();
        var syncBaseDirectory = scope.ServiceProvider.GetRequiredService<IAppDataFactory>().Create().SyncBaseDirectory;

        var pull = await channel.PullAsync();
        pull.Succeeded.Should().BeTrue(pull.Output);
        Read(_sourceWork, "readme.txt").Should().Be("first\n");
        Directory.Exists(syncBaseDirectory).Should().BeFalse("a Git-native Source never needs the snapshot baseline");

        var readyAfterPull = await patches.GetStateAsync();
        readyAfterPull.BaselineInitialized.Should().BeTrue("a resolved Dest tracking ref is enough to be ready to send");
        readyAfterPull.PendingChanges.Should().BeEmpty("nothing changed since the pull");

        Write(_sourceWork, "readme.txt", "edited on Source\n");
        var afterEdit = await patches.GetStateAsync();
        afterEdit.PendingChanges.Select(change => change.Path).Should().Equal("readme.txt");

        var send = await patches.SendAsync();

        send.Sent.Should().BeTrue(send.Message);
        send.Result!.Status.Should().Be(Core.Enums.PatchStatus.Applied, send.Result.Error);
        Read(_destWork, "readme.txt").Should().Be("edited on Source\n");
        Directory.Exists(syncBaseDirectory).Should().BeFalse("sending never had to fall back to the snapshot mechanism");

        // The patch channel writes straight to Dest's files without committing, so the comparison point (the branch last
        // pulled from Dest) does not include this change yet: the file legitimately stays pending until Dest commits it and
        // Source pulls that commit. This is a real behavioral consequence of comparing against Git truth, not a bug.
        var readyRightAfterSend = await patches.GetStateAsync();
        readyRightAfterSend.PendingChanges.Select(change => change.Path).Should().Equal("readme.txt");

        await RunGitAsync(_destWork, "add", "-A");
        await RunGitAsync(_destWork, "-c", "user.name=tester", "-c", "user.email=tester@example.com", "commit", "--quiet", "-m", "commit what Source sent");

        // Source's own edit was never committed there either (the patch channel only ever sent its bytes). Committing the
        // same content independently on both sides would diverge the histories instead of fast-forwarding, so instead the
        // operator on Source discards the now-redundant local edit, expecting it back through the pull below.
        await RunGitAsync(_sourceWork, "checkout", "--", "readme.txt");

        var secondPull = await channel.PullAsync();
        secondPull.Succeeded.Should().BeTrue(secondPull.Output);

        var readyAfterDestCommits = await patches.GetStateAsync();
        readyAfterDestCommits.PendingChanges.Should().BeEmpty("once Dest commits what it received and Source pulls that commit, Git truth agrees again");
    }

    /// <summary>
    /// Reads the certificate fingerprint that Dest published in its export file.
    /// </summary>
    /// <returns>The fingerprint.</returns>
    private string FingerprintOfDest()
    {
        using var certificate = NodeCertificateStore.GetOrCreate(_dest!.Services.GetRequiredService<IAppDataFactory>().Create(), "dest-1");
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
    /// Checks whether Git is installed.
    /// </summary>
    /// <returns><see langword="true"/> when <c>git --version</c> works.</returns>
    private static async Task<bool> GitAvailableAsync()
    {
        try
        {
            return (await new SystemProcessRunner().RunAsync(new RCynQ.Core.Abstractions.Processes.ProcessSpec("git", new[] { "--version" }, null, null, TimeSpan.FromSeconds(30)))).ExitCode == 0;
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
    private static string Read(string directory, string relative) => File.ReadAllText(Path.Combine(directory, relative));
}
