using System.Globalization;
using System.Security.Cryptography;
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
using RCynQ.Sync.Commits;
using RCynQ.Sync.Endpoint;
using RCynQ.Sync.GitChannel;
using RCynQ.Sync.Services;

namespace RCynQ.Service.Tests;

/// <summary>
/// End-to-end tests of the delivery of commits: a Workstation sends the commits of its branch to an Origin that fast-forwards
/// its checked out branch to them. Two real services run on Kestrel with real Git repositories; only the SSH forward is
/// replaced by a direct connection to the TLS port of the Origin. They return early when <c>git</c> is not installed.
/// </summary>
public sealed class CommitDeliveryEndToEndTests : IAsyncLifetime
{
    /// <summary>The Origin service.</summary>
    private ServiceTestHost? _origin;

    /// <summary>The Workstation service.</summary>
    private ServiceTestHost? _workstation;

    /// <summary>The Git repository of the Origin.</summary>
    private string _originWork = null!;

    /// <summary>The working tree of the Workstation.</summary>
    private string _workstationWork = null!;

    /// <summary>The chain repository substitute of the Workstation, pointed at the TLS port of the Origin.</summary>
    private readonly IChainLinkRepository _chain = Substitute.For<IChainLinkRepository>();

    /// <summary>The forward manager substitute of the Workstation, replaced by a direct connection.</summary>
    private readonly ILocalForwardManager _forwards = Substitute.For<ILocalForwardManager>();

    /// <summary>The Git command runner used to prepare and inspect the repositories.</summary>
    private readonly GitCommandRunner _git = new(new SystemProcessRunner(TestTexts.English), new GitOptions());

    /// <summary>
    /// Starts the Origin with a repository of one commit and the Workstation pointed at it, and clones it by Pull.
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
        await CommitAsync(_originWork, "first commit", "origin");

        _workstation = await ServiceTestHost.StartAsync(
            NodeRole.Workstation,
            configureServices: services =>
            {
                services.AddSingleton(_chain);
                services.AddSingleton(_forwards);
            });
        _workstationWork = Path.Combine(_workstation.Root, "work");

        var workstationSignature = _workstation.Services.GetRequiredService<Core.Abstractions.Security.ISignatureService>();
        var key = Convert.ToBase64String(await workstationSignature.ExportPublicKeyAsync());
        await _origin.Services.GetRequiredService<IPeerRepository>()
            .UpsertAsync(new PeerDescriptor(new NodeId("workstation-1"), "Workstation", NodeRole.Workstation, "workstation.local", 22, "rcynq", signingPublicKey: key));

        string fingerprint;
        using (var certificate = NodeCertificateStore.GetOrCreate(_origin.Services.GetRequiredService<IAppDataFactory>().Create(), "origin-1"))
        {
            fingerprint = NodeCertificateStore.Fingerprint(certificate);
        }

        _chain.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new[]
        {
            new ChainLinkConfig(
                Guid.NewGuid(), "origin", TransportKind.Direct,
                new PeerDescriptor(new NodeId("origin-1"), "Origin", NodeRole.Origin, "127.0.0.1", 22, "rcynq", endpointMode: EndpointMode.Direct, tlsFingerprint: fingerprint, endpointPort: tlsPort),
                0, tlsPort, 1000)
        });
        _forwards.StartAsync(Arg.Any<SshEndpoint>(), Arg.Any<IReadOnlyList<SshEndpoint>>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(tlsPort);

        var pull = await _workstation.Services.GetRequiredService<GitChannelService>().PullAsync();
        pull.Succeeded.Should().BeTrue(pull.Output);
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
    /// Verifies that commits arrive as they are: the same hashes and authors on the branch of the Origin, which the
    /// Workstation then has as its own, so nothing is left to send.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Commits_Arrive_As_They_Are_And_Leave_Nothing_Unsent()
    {
        if (_origin is null || _workstation is null)
        {
            return;
        }

        AcceptCommits(true);
        var commits = _workstation.Services.GetRequiredService<ICommitService>();
        Write(_workstationWork, "feature.txt", "one\n");
        await RunGitAsync(_workstationWork, "add", "feature.txt");
        await CommitAsync(_workstationWork, "first feature", "private author");
        Write(_workstationWork, "feature.txt", "two\n");
        await RunGitAsync(_workstationWork, "add", "feature.txt");
        await CommitAsync(_workstationWork, "second feature", "private author");
        var head = await HeadAsync(_workstationWork);
        (await commits.GetUnsentAsync()).Commits.Should().HaveCount(2);

        var send = await commits.SendAsync(null);

        send.Result!.Status.Should().Be(PatchStatus.Applied, send.Message);
        (await HeadAsync(_originWork)).Should().Be(head, "the commits keep their hashes");
        (await OutputAsync(_originWork, "log", "-1", "--format=%an")).Trim().Should().Be("private author");
        Read(_originWork, "feature.txt").Should().Be("two\n");
        var unsent = await commits.GetUnsentAsync();
        unsent.State.Should().Be(UnsentCommitsState.Ready);
        unsent.Commits.Should().BeEmpty("the Fetch after the delivery brought the branch of the Origin to the same commit");
        var received = (await _origin.Services.GetRequiredService<IPatchService>().GetRecentAsync(10)).Single();
        received.Status.Should().Be(PatchStatus.Applied);
        received.Commits!.Commits.Should().HaveCount(2);
    }

    /// <summary>
    /// Verifies that an Origin that does not take commits refuses them, the refusal is in the history of the Workstation,
    /// and not one file under <c>.git</c> of the Origin changes.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Refused_Commits_Leave_The_Repository_Of_The_Origin_Untouched()
    {
        if (_origin is null || _workstation is null)
        {
            return;
        }

        var commits = _workstation.Services.GetRequiredService<ICommitService>();
        Write(_workstationWork, "feature.txt", "one\n");
        await RunGitAsync(_workstationWork, "add", "feature.txt");
        await CommitAsync(_workstationWork, "feature", "dev");
        var before = Fingerprint(Path.Combine(_originWork, ".git"));

        var send = await commits.SendAsync(null);

        send.Result!.Status.Should().Be(PatchStatus.Failed);
        send.Message.Should().Contain(TestTexts.English.Get("Commits.NotAccepted"));
        Fingerprint(Path.Combine(_originWork, ".git")).Should().Equal(before, "a refused delivery writes nothing into the repository");
        (await commits.GetUnsentAsync()).Commits.Should().HaveCount(1, "the commit is still unsent");
        (await _workstation.Services.GetRequiredService<IPatchService>().GetRecentAsync(1)).Single().Status.Should().Be(PatchStatus.Failed);
    }

    /// <summary>
    /// Verifies that commits wait while the working tree of the Origin has changes, that patches of files are refused
    /// meanwhile, that nothing reaches <c>.git</c> before the commits are applied, and that the Workstation learns of the
    /// apply by its next Fetch.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Commits_Wait_For_A_Clean_Working_Tree()
    {
        if (_origin is null || _workstation is null)
        {
            return;
        }

        AcceptCommits(true);
        var commits = _workstation.Services.GetRequiredService<ICommitService>();
        var originCommits = _origin.Services.GetRequiredService<ICommitService>();
        Write(_workstationWork, "feature.txt", "one\n");
        await RunGitAsync(_workstationWork, "add", "feature.txt");
        await CommitAsync(_workstationWork, "feature", "dev");
        var head = await HeadAsync(_workstationWork);
        Write(_originWork, "scratch.txt", "typed on the Origin\n");
        var before = Fingerprint(Path.Combine(_originWork, ".git"));

        var send = await commits.SendAsync(null);

        send.Result!.Status.Should().Be(PatchStatus.Waiting, send.Message);
        Fingerprint(Path.Combine(_originWork, ".git")).Should().Equal(before, "waiting commits stay outside the repository");
        Write(_workstationWork, "readme.txt", "edited on the Workstation\n");
        var patch = await _workstation.Services.GetRequiredService<IPatchService>().SendAsync();
        patch.Result!.Status.Should().Be(PatchStatus.Failed, "patches are refused while commits wait");
        File.Delete(Path.Combine(_workstationWork, "readme.txt"));
        await RunGitAsync(_workstationWork, "checkout", "--", "readme.txt");

        var waiting = (await _origin.Services.GetRequiredService<IPatchService>().GetRecentAsync(10)).Single(item => item.Status == PatchStatus.Waiting);
        (await originCommits.ApplyWaitingAsync(waiting.Id)).Status.Should().Be(PatchStatus.Waiting, "the working tree still has changes");
        File.Delete(Path.Combine(_originWork, "scratch.txt"));
        var applied = await originCommits.ApplyWaitingAsync(waiting.Id);

        applied.Status.Should().Be(PatchStatus.Applied, applied.Error);
        (await HeadAsync(_originWork)).Should().Be(head);
        (await _workstation.Services.GetRequiredService<GitChannelService>().FetchAsync()).Succeeded.Should().BeTrue();
        (await _workstation.Services.GetRequiredService<IPatchService>().GetAsync(send.PatchId!.Value))!.Status
            .Should().Be(PatchStatus.Applied, "the Fetch shows the Origin applied the commits");
    }

    /// <summary>
    /// Verifies that commits built on another commit than the one checked out on the Origin are refused at once.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Commits_Built_On_Another_Commit_Are_Refused()
    {
        if (_origin is null || _workstation is null)
        {
            return;
        }

        AcceptCommits(true);
        var commits = _workstation.Services.GetRequiredService<ICommitService>();
        Write(_originWork, "readme.txt", "moved on the Origin\n");
        await CommitAsync(_originWork, "origin moves on", "origin", "-a");
        Write(_workstationWork, "feature.txt", "one\n");
        await RunGitAsync(_workstationWork, "add", "feature.txt");
        await CommitAsync(_workstationWork, "feature", "dev");
        var before = Fingerprint(Path.Combine(_originWork, ".git"));

        var send = await commits.SendAsync(null);

        send.Result!.Status.Should().Be(PatchStatus.Failed);
        send.Message.Should().Contain("main");
        Fingerprint(Path.Combine(_originWork, ".git")).Should().Equal(before);
    }

    /// <summary>
    /// Verifies that a rollback is refused while the working tree has any change, or when the branch moved, or to a commit
    /// that is not an unsent one, and works once the tree is clean.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Rollback_Waits_For_A_Clean_Working_Tree()
    {
        if (_origin is null || _workstation is null)
        {
            return;
        }

        var commits = _workstation.Services.GetRequiredService<ICommitService>();
        var origin = await HeadAsync(_workstationWork);
        Write(_workstationWork, "feature.txt", "one\n");
        await RunGitAsync(_workstationWork, "add", "feature.txt");
        await CommitAsync(_workstationWork, "first feature", "dev");
        Write(_workstationWork, "feature.txt", "two\n");
        await RunGitAsync(_workstationWork, "add", "feature.txt");
        await CommitAsync(_workstationWork, "second feature", "dev");
        var head = await HeadAsync(_workstationWork);
        Write(_workstationWork, "untracked.txt", "not committed\n");

        var refused = await commits.ResetAsync(origin, CommitResetMode.Soft, head);
        refused.Succeeded.Should().BeFalse();
        refused.Output.Should().Contain("untracked.txt");
        (await HeadAsync(_workstationWork)).Should().Be(head);

        File.Delete(Path.Combine(_workstationWork, "untracked.txt"));
        var stale = await commits.ResetAsync(origin, CommitResetMode.Soft, origin);
        stale.Succeeded.Should().BeFalse("the branch is not where the user saw it");
        var elsewhere = await commits.ResetAsync(new string('a', 40), CommitResetMode.Hard, head);
        elsewhere.Succeeded.Should().BeFalse("only the unsent commits and the commit of the Origin are places to roll back to");

        var done = await commits.ResetAsync(origin, CommitResetMode.Soft, head);

        done.Succeeded.Should().BeTrue(done.Output);
        (await HeadAsync(_workstationWork)).Should().Be(origin);
        (await OutputAsync(_workstationWork, "diff", "--cached", "--name-only")).Trim().Should().Be("feature.txt", "Soft keeps the changes staged");
    }

    /// <summary>
    /// Switches whether the Origin takes commits.
    /// </summary>
    /// <param name="enabled">Whether it takes them.</param>
    private void AcceptCommits(bool enabled) => _origin!.Services.GetRequiredService<CommitAcceptance>().IsEnabled = enabled;

    /// <summary>
    /// Commits in a repository with the given author.
    /// </summary>
    /// <param name="directory">The repository.</param>
    /// <param name="message">The message.</param>
    /// <param name="author">The name of the author.</param>
    /// <param name="extra">More arguments of <c>git commit</c>.</param>
    /// <returns>A task representing the operation.</returns>
    private Task CommitAsync(string directory, string message, string author, params string[] extra)
        => RunGitAsync(directory, new[] { "-c", $"user.name={author}", "-c", "user.email=dev@example.com", "commit", "--quiet", "-m", message }.Concat(extra).ToArray());

    /// <summary>
    /// Reads the commit checked out.
    /// </summary>
    /// <param name="directory">The repository.</param>
    /// <returns>The full commit name.</returns>
    private async Task<string> HeadAsync(string directory) => (await OutputAsync(directory, "rev-parse", "HEAD")).Trim();

    /// <summary>
    /// Runs git and asserts success.
    /// </summary>
    /// <param name="directory">The working directory.</param>
    /// <param name="arguments">The arguments.</param>
    /// <returns>A task representing the operation.</returns>
    private async Task RunGitAsync(string directory, params string[] arguments) => await OutputAsync(directory, arguments);

    /// <summary>
    /// Runs git, asserts success and returns its output.
    /// </summary>
    /// <param name="directory">The working directory.</param>
    /// <param name="arguments">The arguments.</param>
    /// <returns>The standard output.</returns>
    private async Task<string> OutputAsync(string directory, params string[] arguments)
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
    /// Lists every file under a directory with the hash of its content.
    /// </summary>
    /// <param name="directory">The directory.</param>
    /// <returns>The relative paths with their hashes, in order.</returns>
    private static string[] Fingerprint(string directory)
        => Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(file => Path.GetRelativePath(directory, file) + " " + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))))
            .ToArray();

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
