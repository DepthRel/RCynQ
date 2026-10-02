using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using RCynQ.Core.Abstractions.Processes;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Abstractions.Ssh;
using RCynQ.Core.Abstractions.Storage;
using RCynQ.Core.Config;
using RCynQ.Core.Data.Storage;
using RCynQ.Core.Enums;
using RCynQ.Core.Ids;
using RCynQ.Git.Commands;
using RCynQ.Git.Manifest;
using RCynQ.Git.Paths;
using RCynQ.Git.Processes;
using RCynQ.Git.Repository;
using RCynQ.Service.Tests.Support;
using RCynQ.Ssh.Commands;
using RCynQ.Sync.Endpoint;
using RCynQ.Sync.GitChannel;
using RCynQ.Sync.Locking;
using RCynQ.Sync.Paths;
using RCynQ.Sync.Security;
using RCynQ.Sync.SyncBase;
using RCynQ.Sync.Transfer;

namespace RCynQ.Service.Tests;

/// <summary>
/// End-to-end tests of the pull of a Dest without an SSH server: a real Dest service with a real Git repository and its own TLS port,
/// asked for a bundle by the real client of a Source, through a mediator that is replaced by a direct connection. They return early
/// when <c>git</c> is not installed.
/// </summary>
public sealed class GitBundlePullEndToEndTests : IAsyncLifetime
{
    /// <summary>The Dest service.</summary>
    private ServiceTestHost? _host;

    /// <summary>The Git repository of Dest.</summary>
    private string _destRepo = null!;

    /// <summary>The temporary root of the simulated Source.</summary>
    private string _sourceRoot = null!;

    /// <summary>The working tree of Source.</summary>
    private string _sourceWork = null!;

    /// <summary>The TLS port of the Dest endpoint.</summary>
    private int _tlsPort;

    /// <summary>The Git command runner.</summary>
    private readonly GitCommandRunner _git = new(new SystemProcessRunner(TestTexts.English), new GitOptions());

    /// <summary>The forward manager, replaced by a direct connection.</summary>
    private readonly ILocalForwardManager _forwards = Substitute.For<ILocalForwardManager>();

    /// <summary>The working tree lock of the simulated Source.</summary>
    private readonly WorkingTreeLock _sourceLock = new();

    /// <summary>The sync base manager of the simulated Source, created on first use.</summary>
    private SyncBaseManager? _sourceSyncBase;

    /// <summary>
    /// Starts Dest with a repository and registers Source as its peer.
    /// </summary>
    /// <returns>A task representing the setup.</returns>
    public async Task InitializeAsync()
    {
        if (!await GitAvailableAsync())
        {
            return;
        }

        _tlsPort = ServiceTestHost.FreePort();
        _host = await ServiceTestHost.StartAsync(
            NodeRole.Dest,
            extraSettings: new Dictionary<string, string?>
            {
                ["SyncBridge:Endpoint:Mode"] = "Direct",
                ["SyncBridge:Endpoint:BindAddress"] = "127.0.0.1",
                ["SyncBridge:Endpoint:Port"] = _tlsPort.ToString(System.Globalization.CultureInfo.InvariantCulture)
            });
        _destRepo = Path.Combine(_host.Root, "work");
        _sourceRoot = Path.Combine(Path.GetTempPath(), "rcynq-bundle-e2e", Guid.NewGuid().ToString("N"));
        _sourceWork = Directory.CreateDirectory(Path.Combine(_sourceRoot, "work")).FullName;

        await RunGitAsync(_destRepo, "init", "--quiet", "--initial-branch=main");
        Write(_destRepo, "readme.txt", "first\n");
        Write(_destRepo, "src/app.cs", "class App {}\n");
        await CommitAsync("first commit");

        using var scope = _host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IPeerRepository>()
            .UpsertAsync(new PeerDescriptor(new NodeId("source-1"), "Source", NodeRole.Source, "source.local", 22, "rcynq"));
        _forwards.StartAsync(Arg.Any<SshEndpoint>(), Arg.Any<IReadOnlyList<SshEndpoint>>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_tlsPort);
    }

    /// <summary>
    /// Stops the service and deletes the files.
    /// </summary>
    /// <returns>A task representing the cleanup.</returns>
    public async Task DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.DisposeAsync();
        }

        try
        {
            if (_sourceRoot is not null)
            {
                Directory.Delete(_sourceRoot, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Git keeps object files read-only on some systems; best effort cleanup.
        }
    }

    /// <summary>
    /// Verifies that an empty folder becomes a clone of Dest, and that a later pull brings only the new commit.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Empty_Folder_Becomes_A_Clone_And_Later_Pulls_Bring_New_Commits()
    {
        if (_host is null)
        {
            return;
        }

        var channel = CreateChannel();

        var first = await channel.PullAsync();

        first.Succeeded.Should().BeTrue(first.Output);
        Read(_sourceWork, "readme.txt").Should().Be("first\n");
        Read(_sourceWork, "src/app.cs").Should().Be("class App {}\n");
        (await CountCommitsAsync(_sourceWork)).Should().Be(1);

        Write(_destRepo, "readme.txt", "second\n");
        Write(_destRepo, "docs/new.md", "new file\n");
        await CommitAsync("second commit");
        var second = await channel.PullAsync();

        second.Succeeded.Should().BeTrue(second.Output);
        Read(_sourceWork, "readme.txt").Should().Be("second\n");
        Read(_sourceWork, "docs/new.md").Should().Be("new file\n");
        (await CountCommitsAsync(_sourceWork)).Should().Be(2);
        (await channel.GetBranchAsync()).Should().Match<RCynQ.Core.State.GitBranchInfo>(info => info.Branch == "main" && info.Ahead == 0 && info.Behind == 0);

        var third = await channel.FetchAsync();

        third.Succeeded.Should().BeTrue();
        third.Output.Should().Contain("up to date");
    }

    /// <summary>
    /// Verifies that the <c>dest</c> remote is (re-)registered even when a Pull finds nothing new to fetch — for example a node
    /// set up before remote registration existed, so its very first Pull under the new code lands directly in the "nothing new"
    /// case. Regression test for a bug where the "nothing new" early return skipped remote registration, so <c>git checkout
    /// &lt;branch&gt;</c> and IDEs such as VS Code (<c>git checkout --track dest/&lt;branch&gt;</c>) could never create a local
    /// branch from another branch of Dest, however many times Pull was run afterwards.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Remote_Is_Registered_Even_When_A_Pull_Finds_Nothing_New()
    {
        if (_host is null)
        {
            return;
        }

        await RunGitAsync(_destRepo, "branch", "other", "main");
        var channel = CreateChannel();

        // A first pull the ordinary way: brings everything, including refs/remotes/dest/other.
        (await channel.PullAsync()).Succeeded.Should().BeTrue();

        // Simulate the remote config being absent going into a pull that finds nothing new, leaving the already-fetched
        // refs/remotes/dest/* untouched — the exact situation reported against RCynQ: an installation whose earlier pulls ran
        // before this fix, so "dest" was never registered, and every later pull (with nothing new on Dest) kept finding nothing
        // to do and kept skipping registration. ("git remote remove" would also delete the tracking refs, unlike the real bug.)
        (await _git.RunAsync(new[] { "config", "--remove-section", "remote.dest" }, _sourceWork)).ExitCode.Should().Be(0);

        var second = await channel.PullAsync();

        second.Succeeded.Should().BeTrue(second.Output);
        second.Output.Should().Contain("up to date", "nothing changed on Dest since the first pull");
        (await RunGitAsync(_sourceWork, "remote", "get-url", "dest")).Trim().Should().NotBeEmpty("the remote must be registered even though nothing was fetched this time");
        (await _git.RunAsync(new[] { "checkout", "-q", "--track", "dest/other" }, _sourceWork)).ExitCode.Should().Be(0, "Git can only do this once the remote is registered");
    }

    /// <summary>
    /// Verifies that a folder that already has the files but no repository keeps them: the differences to Dest show up as local changes.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Existing_Files_Without_A_Repository_Are_Kept()
    {
        if (_host is null)
        {
            return;
        }

        Write(_sourceWork, "readme.txt", "first\n");
        Write(_sourceWork, "src/app.cs", "class App { /* edited on the Source */ }\n");
        Write(_sourceWork, "local-only.txt", "never in Dest\n");

        var result = await CreateChannel().PullAsync();

        result.Succeeded.Should().BeTrue(result.Output);
        Read(_sourceWork, "src/app.cs").Should().Be("class App { /* edited on the Source */ }\n", "the edit must not be lost");
        Read(_sourceWork, "local-only.txt").Should().Be("never in Dest\n");
        var status = await RunGitAsync(_sourceWork, "status", "--porcelain");
        status.Should().Contain("src/app.cs").And.Contain("local-only.txt").And.NotContain("readme.txt");
    }

    /// <summary>
    /// Verifies that a repository that lags behind Dest gets only what is missing and that local commits do not confuse the request.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Lagging_Repository_With_Local_Commits_Is_Brought_Up_To_Date()
    {
        if (_host is null)
        {
            return;
        }

        var channel = CreateChannel();
        (await channel.PullAsync()).Succeeded.Should().BeTrue();
        Write(_sourceWork, "mine.txt", "a commit only the Source has\n");
        await RunGitAsync(_sourceWork, "add", "-A");
        await RunGitAsync(_sourceWork, "-c", "user.name=t", "-c", "user.email=t@t", "commit", "--quiet", "-m", "local commit");
        Write(_destRepo, "src/app.cs", "class App { int Version; }\n");
        await CommitAsync("dest moves on");

        var fetch = await channel.FetchAsync();

        fetch.Succeeded.Should().BeTrue(fetch.Output);
        fetch.Branch.Should().Match<RCynQ.Core.State.GitBranchInfo>(info => info.Ahead == 1 && info.Behind == 1, "the histories diverged, which is reported instead of being merged");
        (await RunGitAsync(_sourceWork, "show", "refs/remotes/dest/main:src/app.cs")).Should().Be("class App { int Version; }\n");
    }

    /// <summary>
    /// Verifies that a request with a wrong link token is refused and creates nothing.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Wrong_Link_Token_Is_Refused()
    {
        if (_host is null)
        {
            return;
        }

        var result = await CreateChannel("not-the-token").FetchAsync();

        result.Succeeded.Should().BeFalse();
        result.Output.Should().Contain("InvalidToken");
        Directory.Exists(Path.Combine(_sourceWork, ".git")).Should().BeFalse();
    }

    /// <summary>
    /// Verifies that a pull brings the sync base along: pulled files are not offered for sending back, while edits of the user stay pending.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Pull_Keeps_The_Sync_Base_In_Line_And_User_Edits_Stay_Pending()
    {
        if (_host is null)
        {
            return;
        }

        var channel = CreateChannel();
        (await channel.PullAsync()).Succeeded.Should().BeTrue();
        var syncBase = SourcePaths().SyncBase;
        Read(syncBase, "readme.txt").Should().Be("first\n", "the first Pull builds the sync base from the cloned commit");

        Write(_sourceWork, "my-notes.txt", "typed on the Source\n");
        Write(_destRepo, "readme.txt", "second\n");
        Write(_destRepo, "docs/new.md", "new file\n");
        File.Delete(Path.Combine(_destRepo, "src", "app.cs"));
        await CommitAsync("dest moves on");

        var pull = await channel.PullAsync();

        pull.Succeeded.Should().BeTrue(pull.Output);
        pull.Output.Should().Contain("sync base now follows commit");
        Read(syncBase, "readme.txt").Should().Be("second\n");
        Read(syncBase, "docs/new.md").Should().Be("new file\n");
        File.Exists(Path.Combine(syncBase, "src", "app.cs")).Should().BeFalse();
        (await PendingAsync()).Should().Equal("my-notes.txt");
    }

    /// <summary>
    /// Verifies that a local edit Dest already has in its new commit does not block the Pull: it is replaced by Dest's version.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Pull_Replaces_A_Local_Edit_That_Dest_Already_Committed()
    {
        if (_host is null)
        {
            return;
        }

        var channel = CreateChannel();
        (await channel.PullAsync()).Succeeded.Should().BeTrue();
        Write(_sourceWork, "readme.txt", "second\n");
        Write(_destRepo, "readme.txt", "second\n");
        await CommitAsync("the same edit on Dest");

        var pull = await channel.PullAsync();

        pull.Succeeded.Should().BeTrue(pull.Output);
        pull.Branch!.Behind.Should().Be(0);
        Read(_sourceWork, "readme.txt").Should().Be("second\n");
        // As the user's own git status sees it: with the line-ending settings of the repository.
        (await _git.RunInWorkingTreeAsync(new[] { "status", "--porcelain" }, _sourceWork)).StandardOutput.Trim().Should().BeEmpty();
        (await PendingAsync()).Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that a Pull that would overwrite a local edit not yet sent to Dest is refused, lists the file and changes nothing.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Pull_Refuses_To_Overwrite_An_Unsent_Local_Edit()
    {
        if (_host is null)
        {
            return;
        }

        var channel = CreateChannel();
        (await channel.PullAsync()).Succeeded.Should().BeTrue();
        var before = (await RunGitAsync(_sourceWork, "rev-parse", "HEAD")).Trim();
        Write(_sourceWork, "readme.txt", "mine, not sent yet\n");
        Write(_destRepo, "readme.txt", "second\n");
        await CommitAsync("dest edits the same file");

        var pull = await channel.PullAsync();

        pull.Succeeded.Should().BeFalse();
        pull.Output.Should().Contain("readme.txt").And.Contain("not been sent");
        Read(_sourceWork, "readme.txt").Should().Be("mine, not sent yet\n");
        (await RunGitAsync(_sourceWork, "rev-parse", "HEAD")).Trim().Should().Be(before);
        pull.Branch!.Behind.Should().Be(1, "the fetch still brought the new commit in");
    }

    /// <summary>
    /// Verifies that a Fetch alone does not move the sync base, so Dest's new commit is not offered back as a revert.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Fetch_Alone_Does_Not_Offer_Dest_Commits_Back()
    {
        if (_host is null)
        {
            return;
        }

        var channel = CreateChannel();
        (await channel.PullAsync()).Succeeded.Should().BeTrue();
        Write(_destRepo, "readme.txt", "second\n");
        await CommitAsync("dest moves on");

        (await channel.FetchAsync()).Succeeded.Should().BeTrue();

        (await PendingAsync()).Should().BeEmpty();
    }

    /// <summary>
    /// Brings the sync base of the simulated Source up to date and lists the files it would send.
    /// </summary>
    /// <returns>The pending paths.</returns>
    private async Task<IReadOnlyList<string>> PendingAsync()
    {
        using (await _sourceLock.AcquireAsync())
        {
            var status = await SourceSyncBase().EnsureAsync();
            status.IsReady.Should().BeTrue(status.Problem);
            var pending = await new DefaultFileManifestBuilder(new SnapshotFilter()).BuildAsync(SourcePaths().SyncBase, _sourceWork);
            return pending.Select(change => change.Path).ToArray();
        }
    }

    /// <summary>
    /// Gets the node of the simulated Source.
    /// </summary>
    /// <returns>The node.</returns>
    private NodeConfig SourceNode() => new(new NodeId("source-1"), "Source", NodeRole.Source, null, _sourceWork);

    /// <summary>
    /// Gets the sync paths of the simulated Source.
    /// </summary>
    /// <returns>The paths.</returns>
    private SyncPaths SourcePaths() => new(TestTexts.English, SourceAppData(), SourceNode());

    /// <summary>
    /// Gets the sync base manager of the simulated Source, shared by every channel of a test.
    /// </summary>
    /// <returns>The manager.</returns>
    private SyncBaseManager SourceSyncBase()
    {
        var git = new GitRepositoryService(TestTexts.English, _git);
        return _sourceSyncBase ??= new SyncBaseManager(
            TestTexts.English,
            SourceNode(),
            SourcePaths(),
            new GitSyncBaseLocator(TestTexts.English, SourceNode(), git),
            git,
            new SyncBaseMetadataStore(SourceAppData()),
            new DefaultFileManifestBuilder(new SnapshotFilter()),
            new RCynQ.Git.Hashing.DefaultStateHasher(),
            Substitute.For<ISnapshotRepository>(),
            new PathUtility(TestTexts.English),
            _sourceLock,
            TimeProvider.System);
    }

    /// <summary>
    /// Creates the data directories of the simulated Source.
    /// </summary>
    /// <returns>The factory.</returns>
    private AppDataFactory SourceAppData() => new(AppDataPaths.FromRoot(Path.Combine(_sourceRoot, "data")));

    /// <summary>
    /// Creates the Git channel of the simulated Source.
    /// </summary>
    /// <param name="token">The link token to present.</param>
    /// <returns>The channel.</returns>
    private GitChannelService CreateChannel(string token = ServiceTestHost.LinkToken)
    {
        var node = SourceNode();
        var fingerprint = FingerprintOfDest();
        var chain = new[]
        {
            new ChainLinkConfig(Guid.NewGuid(), "relay", TransportKind.Direct, new PeerDescriptor(new NodeId("mediator-1"), "Relay", NodeRole.Mediator, "relay.example", 22, "jump"), 0, 22, 10),
            new ChainLinkConfig(
                Guid.NewGuid(), "dest", TransportKind.Direct,
                new PeerDescriptor(new NodeId("dest-1"), "Dest", NodeRole.Dest, "dest.internal", 22, "rcynq", endpointMode: EndpointMode.Direct, tlsFingerprint: fingerprint, endpointPort: _tlsPort),
                0, _tlsPort, 1000)
        };
        var links = Substitute.For<IChainLinkRepository>();
        links.GetAllAsync(Arg.Any<CancellationToken>()).Returns(chain);
        var bundles = new GitBundleClient(
            TestTexts.English,
            new PeerLinkFactory(new SocketsHttpHandler { UseProxy = false }, _forwards),
            node,
            new LinkTokenProvider(new SyncBridgeOptions { LinkToken = token }, new InMemorySecretStore()));
        return new GitChannelService(
            TestTexts.English,
            node,
            links,
            new GitRepositoryService(TestTexts.English, _git),
            new SshCommandBuilder(new SshClientOptions()),
            bundles,
            SourceAppData(),
            SourceSyncBase(),
            SourcePaths(),
            new PathUtility(TestTexts.English),
            _sourceLock);
    }

    /// <summary>
    /// Reads the certificate fingerprint that Dest published in its export file.
    /// </summary>
    /// <returns>The fingerprint.</returns>
    private string FingerprintOfDest()
    {
        using var certificate = NodeCertificateStore.GetOrCreate(_host!.Services.GetRequiredService<IAppDataFactory>().Create(), "dest-1");
        return NodeCertificateStore.Fingerprint(certificate);
    }

    /// <summary>
    /// Commits everything in the Dest repository.
    /// </summary>
    /// <param name="message">The commit message.</param>
    /// <returns>A task representing the commit.</returns>
    private async Task CommitAsync(string message)
    {
        await RunGitAsync(_destRepo, "add", "-A");
        await RunGitAsync(_destRepo, "-c", "user.name=tester", "-c", "user.email=tester@example.com", "commit", "--quiet", "-m", message);
    }

    /// <summary>
    /// Counts the commits of a repository.
    /// </summary>
    /// <param name="directory">The repository.</param>
    /// <returns>The number of commits reachable from HEAD.</returns>
    private async Task<int> CountCommitsAsync(string directory)
        => int.Parse((await RunGitAsync(directory, "rev-list", "--count", "HEAD")).Trim(), System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Runs Git and returns its standard output.
    /// </summary>
    /// <param name="directory">The working directory.</param>
    /// <param name="arguments">The arguments.</param>
    /// <returns>The standard output.</returns>
    private async Task<string> RunGitAsync(string directory, params string[] arguments)
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
            return (await new SystemProcessRunner(TestTexts.English).RunAsync(new ProcessSpec("git", new[] { "--version" }, null, null, TimeSpan.FromSeconds(30)))).ExitCode == 0;
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
