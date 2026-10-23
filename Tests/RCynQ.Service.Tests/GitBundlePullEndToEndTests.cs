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
using RCynQ.Sync.Endpoint;
using RCynQ.Sync.GitChannel;
using RCynQ.Sync.Locking;
using RCynQ.Sync.Paths;
using RCynQ.Sync.Security;
using RCynQ.Sync.SyncBase;
using RCynQ.Sync.Transfer;

namespace RCynQ.Service.Tests;

/// <summary>
/// End-to-end tests of the pull of an Origin without an SSH server: a real Origin service with a real Git repository and its own TLS port,
/// asked for a bundle by the real client of a Workstation, through a relay that is replaced by a direct connection. They return early
/// when <c>git</c> is not installed.
/// </summary>
public sealed class GitBundlePullEndToEndTests : IAsyncLifetime
{
    /// <summary>The Origin service.</summary>
    private ServiceTestHost? _host;

    /// <summary>The Git repository of Origin.</summary>
    private string _originRepo = null!;

    /// <summary>The temporary root of the simulated Workstation.</summary>
    private string _workstationRoot = null!;

    /// <summary>The working tree of Workstation.</summary>
    private string _workstationWork = null!;

    /// <summary>The TLS port of the Origin endpoint.</summary>
    private int _tlsPort;

    /// <summary>The Git command runner.</summary>
    private readonly GitCommandRunner _git = new(new SystemProcessRunner(TestTexts.English), new GitOptions());

    /// <summary>The forward manager, replaced by a direct connection.</summary>
    private readonly ILocalForwardManager _forwards = Substitute.For<ILocalForwardManager>();

    /// <summary>The working tree lock of the simulated Workstation.</summary>
    private readonly WorkingTreeLock _workstationLock = new();

    /// <summary>The sync base manager of the simulated Workstation, created on first use.</summary>
    private SyncBaseManager? _workstationSyncBase;

    /// <summary>
    /// Starts Origin with a repository and registers Workstation as its peer.
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
            NodeRole.Origin,
            extraSettings: new Dictionary<string, string?>
            {
                ["SyncBridge:Endpoint:Mode"] = "Direct",
                ["SyncBridge:Endpoint:BindAddress"] = "127.0.0.1",
                ["SyncBridge:Endpoint:Port"] = _tlsPort.ToString(System.Globalization.CultureInfo.InvariantCulture)
            });
        _originRepo = Path.Combine(_host.Root, "work");
        _workstationRoot = Path.Combine(Path.GetTempPath(), "rcynq-bundle-e2e", Guid.NewGuid().ToString("N"));
        _workstationWork = Directory.CreateDirectory(Path.Combine(_workstationRoot, "work")).FullName;

        await RunGitAsync(_originRepo, "init", "--quiet", "--initial-branch=main");
        Write(_originRepo, "readme.txt", "first\n");
        Write(_originRepo, "src/app.cs", "class App {}\n");
        await CommitAsync("first commit");

        using var scope = _host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IPeerRepository>()
            .UpsertAsync(new PeerDescriptor(new NodeId("workstation-1"), "Workstation", NodeRole.Workstation, "workstation.local", 22, "rcynq"));
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
            if (_workstationRoot is not null)
            {
                Directory.Delete(_workstationRoot, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Git keeps object files read-only on some systems; best effort cleanup.
        }
    }

    /// <summary>
    /// Verifies that an empty folder becomes a clone of Origin, and that a later pull brings only the new commit.
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
        Read(_workstationWork, "readme.txt").Should().Be("first\n");
        Read(_workstationWork, "src/app.cs").Should().Be("class App {}\n");
        (await CountCommitsAsync(_workstationWork)).Should().Be(1);

        Write(_originRepo, "readme.txt", "second\n");
        Write(_originRepo, "docs/new.md", "new file\n");
        await CommitAsync("second commit");
        var second = await channel.PullAsync();

        second.Succeeded.Should().BeTrue(second.Output);
        Read(_workstationWork, "readme.txt").Should().Be("second\n");
        Read(_workstationWork, "docs/new.md").Should().Be("new file\n");
        (await CountCommitsAsync(_workstationWork)).Should().Be(2);
        (await channel.GetBranchAsync()).Should().Match<RCynQ.Core.State.GitBranchInfo>(info => info.Branch == "main" && info.Ahead == 0 && info.Behind == 0);

        var third = await channel.FetchAsync();

        third.Succeeded.Should().BeTrue();
        third.Output.Should().Contain("up to date");
    }

    /// <summary>
    /// Verifies that the <c>rcynq</c> remote is (re-)registered even when a Pull finds nothing new to fetch — for example a node
    /// set up before remote registration existed, so its very first Pull under the new code lands directly in the "nothing new"
    /// case. Regression test for a bug where the "nothing new" early return skipped remote registration, so <c>git checkout
    /// &lt;branch&gt;</c> and IDEs such as VS Code (<c>git checkout --track rcynq/&lt;branch&gt;</c>) could never create a local
    /// branch from another branch of Origin, however many times Pull was run afterwards.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Remote_Is_Registered_Even_When_A_Pull_Finds_Nothing_New()
    {
        if (_host is null)
        {
            return;
        }

        await RunGitAsync(_originRepo, "branch", "other", "main");
        var channel = CreateChannel();

        // A first pull the ordinary way: brings everything, including refs/remotes/rcynq/other.
        (await channel.PullAsync()).Succeeded.Should().BeTrue();

        // Simulate the remote config being absent going into a pull that finds nothing new, leaving the already-fetched
        // refs/remotes/rcynq/* untouched — the exact situation reported against RCynQ: an installation whose earlier pulls ran
        // before this fix, so "rcynq" was never registered, and every later pull (with nothing new on Origin) kept finding nothing
        // to do and kept skipping registration. ("git remote remove" would also delete the tracking refs, unlike the real bug.)
        (await _git.RunAsync(new[] { "config", "--remove-section", "remote.rcynq" }, _workstationWork)).ExitCode.Should().Be(0);

        var second = await channel.PullAsync();

        second.Succeeded.Should().BeTrue(second.Output);
        second.Output.Should().Contain("up to date", "nothing changed on Origin since the first pull");
        (await RunGitAsync(_workstationWork, "remote", "get-url", "rcynq")).Trim().Should().NotBeEmpty("the remote must be registered even though nothing was fetched this time");
        (await _git.RunAsync(new[] { "checkout", "-q", "--track", "rcynq/other" }, _workstationWork)).ExitCode.Should().Be(0, "Git can only do this once the remote is registered");
    }

    /// <summary>
    /// Verifies that a Fetch brings the branches no bundle carries: a branch created on Origin at a commit the Workstation
    /// has, and a branch moved back. A branch deleted on Origin disappears from <c>refs/remotes/rcynq/*</c>.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Fetch_Brings_Branches_Without_New_Commits_And_Drops_Deleted_Ones()
    {
        if (_host is null)
        {
            return;
        }

        await RunGitAsync(_originRepo, "branch", "old", "main");
        var first = (await RunGitAsync(_originRepo, "rev-parse", "main")).Trim();
        Write(_originRepo, "readme.txt", "second\n");
        await CommitAsync("second commit");
        await RunGitAsync(_originRepo, "branch", "moving", "main");
        var channel = CreateChannel();
        (await channel.PullAsync()).Succeeded.Should().BeTrue();

        await RunGitAsync(_originRepo, "branch", "feature/new", "main");
        await RunGitAsync(_originRepo, "branch", "--force", "moving", first);
        await RunGitAsync(_originRepo, "branch", "--delete", "old");
        var fetch = await channel.FetchAsync();

        fetch.Succeeded.Should().BeTrue(fetch.Output);
        fetch.Output.Should().Contain("feature/new").And.Contain("moving").And.Contain("old");
        (await RunGitAsync(_workstationWork, "rev-parse", "refs/remotes/rcynq/feature/new")).Trim().Should().Be((await RunGitAsync(_originRepo, "rev-parse", "main")).Trim());
        (await RunGitAsync(_workstationWork, "rev-parse", "refs/remotes/rcynq/moving")).Trim().Should().Be(first);
        (await _git.RunAsync(new[] { "rev-parse", "--verify", "--quiet", "refs/remotes/rcynq/old" }, _workstationWork)).ExitCode.Should().NotBe(0);
        (await _git.RunAsync(new[] { "checkout", "-q", "feature/new" }, _workstationWork)).ExitCode.Should().Be(0, "Git creates the local branch from rcynq/feature/new");
    }

    /// <summary>
    /// Verifies that a Pull makes the current branch follow its branch of Origin, so Git does not count the pulled commits
    /// as outgoing against a remote the branch followed before.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Pull_Makes_The_Branch_Follow_Origin_Instead_Of_Another_Remote()
    {
        if (_host is null)
        {
            return;
        }

        var channel = CreateChannel();
        (await channel.PullAsync()).Succeeded.Should().BeTrue();
        var stale = (await RunGitAsync(_workstationWork, "rev-parse", "HEAD")).Trim();
        await RunGitAsync(_workstationWork, "update-ref", "refs/remotes/company/main", stale);
        await RunGitAsync(_workstationWork, "config", "branch.main.remote", "company");
        Write(_originRepo, "readme.txt", "second\n");
        await CommitAsync("origin moves on");

        var pull = await channel.PullAsync();

        pull.Succeeded.Should().BeTrue(pull.Output);
        pull.Output.Should().Contain(TestTexts.English.Format("Git.PullTrackingReplaced", "main", "rcynq", "company/main"));
        (await RunGitAsync(_workstationWork, "config", "--get", "branch.main.remote")).Trim().Should().Be("rcynq");
        (await RunGitAsync(_workstationWork, "config", "--get", "branch.main.merge")).Trim().Should().Be("refs/heads/main");
        (await RunGitAsync(_workstationWork, "rev-list", "--count", "@{upstream}..HEAD")).Trim().Should().Be("0", "nothing pulled from Origin is outgoing");
        (await channel.PullAsync()).Output.Should().NotContain("rcynq/main instead", "a branch that follows Origin already is left alone");
    }

    /// <summary>
    /// Verifies that a folder that already has the files but no repository keeps them: the differences to Origin show up as local changes.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Existing_Files_Without_A_Repository_Are_Kept()
    {
        if (_host is null)
        {
            return;
        }

        Write(_workstationWork, "readme.txt", "first\n");
        Write(_workstationWork, "src/app.cs", "class App { /* edited on the Workstation */ }\n");
        Write(_workstationWork, "local-only.txt", "never in Origin\n");

        var result = await CreateChannel().PullAsync();

        result.Succeeded.Should().BeTrue(result.Output);
        Read(_workstationWork, "src/app.cs").Should().Be("class App { /* edited on the Workstation */ }\n", "the edit must not be lost");
        Read(_workstationWork, "local-only.txt").Should().Be("never in Origin\n");
        var status = await RunGitAsync(_workstationWork, "status", "--porcelain");
        status.Should().Contain("src/app.cs").And.Contain("local-only.txt").And.NotContain("readme.txt");
    }

    /// <summary>
    /// Verifies that a repository that lags behind Origin gets only what is missing and that local commits do not confuse the request.
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
        Write(_workstationWork, "mine.txt", "a commit only the Workstation has\n");
        await RunGitAsync(_workstationWork, "add", "-A");
        await RunGitAsync(_workstationWork, "-c", "user.name=t", "-c", "user.email=t@t", "commit", "--quiet", "-m", "local commit");
        Write(_originRepo, "src/app.cs", "class App { int Version; }\n");
        await CommitAsync("origin moves on");

        var fetch = await channel.FetchAsync();

        fetch.Succeeded.Should().BeTrue(fetch.Output);
        fetch.Branch.Should().Match<RCynQ.Core.State.GitBranchInfo>(info => info.Ahead == 1 && info.Behind == 1, "the histories diverged, which is reported instead of being merged");
        (await RunGitAsync(_workstationWork, "show", "refs/remotes/rcynq/main:src/app.cs")).Should().Be("class App { int Version; }\n");
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
        Directory.Exists(Path.Combine(_workstationWork, ".git")).Should().BeFalse();
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
        var syncBase = WorkstationPaths().SyncBase;
        Read(syncBase, "readme.txt").Should().Be("first\n", "the first Pull builds the sync base from the cloned commit");

        Write(_workstationWork, "my-notes.txt", "typed on the Workstation\n");
        Write(_originRepo, "readme.txt", "second\n");
        Write(_originRepo, "docs/new.md", "new file\n");
        File.Delete(Path.Combine(_originRepo, "src", "app.cs"));
        await CommitAsync("origin moves on");

        var pull = await channel.PullAsync();

        pull.Succeeded.Should().BeTrue(pull.Output);
        pull.Output.Should().Contain("sync base now follows commit");
        Read(syncBase, "readme.txt").Should().Be("second\n");
        Read(syncBase, "docs/new.md").Should().Be("new file\n");
        File.Exists(Path.Combine(syncBase, "src", "app.cs")).Should().BeFalse();
        (await PendingAsync()).Should().Equal("my-notes.txt");
    }

    /// <summary>
    /// Verifies that a local edit Origin already has in its new commit does not block the Pull: it is replaced by Origin's version.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Pull_Replaces_A_Local_Edit_That_Origin_Already_Committed()
    {
        if (_host is null)
        {
            return;
        }

        var channel = CreateChannel();
        (await channel.PullAsync()).Succeeded.Should().BeTrue();
        Write(_workstationWork, "readme.txt", "second\n");
        Write(_originRepo, "readme.txt", "second\n");
        await CommitAsync("the same edit on Origin");

        var pull = await channel.PullAsync();

        pull.Succeeded.Should().BeTrue(pull.Output);
        pull.Branch!.Behind.Should().Be(0);
        Read(_workstationWork, "readme.txt").Should().Be("second\n");
        // As the user's own git status sees it: with the line-ending settings of the repository.
        (await _git.RunInWorkingTreeAsync(new[] { "status", "--porcelain" }, _workstationWork)).StandardOutput.Trim().Should().BeEmpty();
        (await PendingAsync()).Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that a Pull that would overwrite a local edit not yet sent to Origin is refused, lists the file and changes nothing.
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
        var before = (await RunGitAsync(_workstationWork, "rev-parse", "HEAD")).Trim();
        Write(_workstationWork, "readme.txt", "mine, not sent yet\n");
        Write(_originRepo, "readme.txt", "second\n");
        await CommitAsync("origin edits the same file");

        var pull = await channel.PullAsync();

        pull.Succeeded.Should().BeFalse();
        pull.Output.Should().Contain("readme.txt").And.Contain("not been sent");
        Read(_workstationWork, "readme.txt").Should().Be("mine, not sent yet\n");
        (await RunGitAsync(_workstationWork, "rev-parse", "HEAD")).Trim().Should().Be(before);
        pull.Branch!.Behind.Should().Be(1, "the fetch still brought the new commit in");
    }

    /// <summary>
    /// Verifies that a Fetch alone does not move the sync base, so Origin's new commit is not offered back as a revert.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Fetch_Alone_Does_Not_Offer_Origin_Commits_Back()
    {
        if (_host is null)
        {
            return;
        }

        var channel = CreateChannel();
        (await channel.PullAsync()).Succeeded.Should().BeTrue();
        Write(_originRepo, "readme.txt", "second\n");
        await CommitAsync("origin moves on");

        (await channel.FetchAsync()).Succeeded.Should().BeTrue();

        (await PendingAsync()).Should().BeEmpty();
    }

    /// <summary>
    /// Brings the sync base of the simulated Workstation up to date and lists the files it would send.
    /// </summary>
    /// <returns>The pending paths.</returns>
    private async Task<IReadOnlyList<string>> PendingAsync()
    {
        using (await _workstationLock.AcquireAsync())
        {
            var status = await WorkstationSyncBase().EnsureAsync();
            status.IsReady.Should().BeTrue(status.Problem);
            var pending = await new DefaultFileManifestBuilder(new SnapshotFilter()).BuildAsync(WorkstationPaths().SyncBase, _workstationWork);
            return pending.Select(change => change.Path).ToArray();
        }
    }

    /// <summary>
    /// Gets the node of the simulated Workstation.
    /// </summary>
    /// <returns>The node.</returns>
    private NodeConfig WorkstationNode() => new(new NodeId("workstation-1"), "Workstation", NodeRole.Workstation, null, _workstationWork);

    /// <summary>
    /// Gets the sync paths of the simulated Workstation.
    /// </summary>
    /// <returns>The paths.</returns>
    private SyncPaths WorkstationPaths() => new(
        TestTexts.English,
        WorkstationAppData(),
        WorkstationNode(),
        new FixedRepository(new RepositoryEntry(RepositoryEntry.DefaultId, ServiceTestHost.REPOSITORY_NAME, _workstationWork)));

    /// <summary>
    /// Gets the sync base manager of the simulated Workstation, shared by every channel of a test.
    /// </summary>
    /// <returns>The manager.</returns>
    private SyncBaseManager WorkstationSyncBase()
    {
        var git = new GitRepositoryService(TestTexts.English, _git);
        return _workstationSyncBase ??= new SyncBaseManager(
            TestTexts.English,
            WorkstationNode(),
            WorkstationPaths(),
            new GitSyncBaseLocator(TestTexts.English, WorkstationNode(), git),
            git,
            new SyncBaseMetadataStore(WorkstationAppData()),
            new DefaultFileManifestBuilder(new SnapshotFilter()),
            new RCynQ.Git.Hashing.DefaultStateHasher(),
            Substitute.For<ISnapshotRepository>(),
            new PathUtility(TestTexts.English),
            _workstationLock,
            TimeProvider.System);
    }

    /// <summary>
    /// Creates the data directories of the simulated Workstation.
    /// </summary>
    /// <returns>The factory.</returns>
    private AppDataFactory WorkstationAppData() => new(AppDataPaths.FromRoot(Path.Combine(_workstationRoot, "data")));

    /// <summary>
    /// Creates the Git channel of the simulated Workstation.
    /// </summary>
    /// <param name="token">The link token to present.</param>
    /// <returns>The channel.</returns>
    private GitChannelService CreateChannel(string token = ServiceTestHost.LINK_TOKEN)
    {
        var node = WorkstationNode();
        var fingerprint = FingerprintOfOrigin();
        var chain = new[]
        {
            new ChainLinkConfig(Guid.NewGuid(), "relay", TransportKind.Direct, new PeerDescriptor(new NodeId("relay-1"), "Relay", NodeRole.Relay, "relay.example", 22, "jump"), 0, 22, 10),
            new ChainLinkConfig(
                Guid.NewGuid(), "origin", TransportKind.Direct,
                new PeerDescriptor(new NodeId("origin-1"), "Origin", NodeRole.Origin, "origin.internal", 22, "rcynq", endpointMode: EndpointMode.Direct, tlsFingerprint: fingerprint, endpointPort: _tlsPort),
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
            bundles,
            WorkstationAppData(),
            WorkstationSyncBase(),
            WorkstationPaths(),
            new PathUtility(TestTexts.English),
            _workstationLock);
    }

    /// <summary>
    /// Reads the certificate fingerprint that Origin published in its export file.
    /// </summary>
    /// <returns>The fingerprint.</returns>
    private string FingerprintOfOrigin()
    {
        using var certificate = NodeCertificateStore.GetOrCreate(_host!.Services.GetRequiredService<IAppDataFactory>().Create(), "origin-1");
        return NodeCertificateStore.Fingerprint(certificate);
    }

    /// <summary>
    /// Commits everything in the Origin repository.
    /// </summary>
    /// <param name="message">The commit message.</param>
    /// <returns>A task representing the commit.</returns>
    private async Task CommitAsync(string message)
    {
        await RunGitAsync(_originRepo, "add", "-A");
        await RunGitAsync(_originRepo, "-c", "user.name=tester", "-c", "user.email=tester@example.com", "commit", "--quiet", "-m", message);
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
