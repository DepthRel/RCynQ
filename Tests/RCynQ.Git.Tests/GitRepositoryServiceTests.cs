using FluentAssertions;
using NSubstitute;
using RCynQ.Core.Abstractions.Processes;
using RCynQ.Core.Config;
using RCynQ.Core.Exceptions;
using RCynQ.Git.Commands;
using RCynQ.Git.Repository;
using RCynQ.Git.Tests.Support;

namespace RCynQ.Git.Tests;

/// <summary>
/// Tests for <see cref="GitRepositoryService"/> with a mocked git runner.
/// </summary>
public class GitRepositoryServiceTests
{
    /// <summary>
    /// The mocked git runner.
    /// </summary>
    private readonly IGitCommandRunner _git = Substitute.For<IGitCommandRunner>();

    /// <summary>
    /// Verifies branch and divergence parsing.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task GetBranchInfo_Parses_Divergence()
    {
        using var dir = new TempDirectory();
        Setup("rev-parse --is-inside-work-tree", 0, "true\n");
        Setup("symbolic-ref --quiet --short HEAD", 0, "feature/x\n");
        Setup("rev-parse --verify --quiet refs/remotes/dest/feature/x", 0, "abc\n");
        Setup("rev-list --left-right --count HEAD...refs/remotes/dest/feature/x", 0, "2\t5\n");

        var info = await new GitRepositoryService(_git).GetBranchInfoAsync(dir.Path, "dest");

        info.Should().Be(new Core.State.GitBranchInfo(true, "feature/x", "refs/remotes/dest/feature/x", 2, 5));
    }

    /// <summary>
    /// Verifies that fetch uses an explicit refspec, never pushes and passes the SSH command through the environment.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Fetch_Uses_Refspec_And_Ssh_Command()
    {
        using var dir = new TempDirectory();
        _git.RunAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<string?>(), Arg.Any<IReadOnlyDictionary<string, string>?>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(0, "", " * [new branch] main -> dest/main"));

        var url = GitRepositoryService.BuildSshUrl(new SshEndpoint("git", "dest.internal", 2222), "/srv/repo");
        var output = await new GitRepositoryService(_git).FetchAsync(dir.Path, url, "dest", "ssh -o BatchMode=yes");

        url.Should().Be("ssh://git@dest.internal:2222/srv/repo");
        output.Should().Contain("dest/main");
        await _git.Received(1).RunAsync(
            Arg.Is<IReadOnlyList<string>>(args => args[0] == "fetch" && args.Contains(url) && args.Last() == "+refs/heads/*:refs/remotes/dest/*" && !args.Contains("push")),
            dir.Path,
            Arg.Is<IReadOnlyDictionary<string, string>?>(env => env!["GIT_SSH_COMMAND"] == "ssh -o BatchMode=yes"),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies rejection of option-like or dangerous remote URLs and unsafe names.
    /// </summary>
    /// <param name="url">The URL.</param>
    /// <param name="remote">The remote name.</param>
    [Theory]
    [InlineData("--upload-pack=touch /tmp/x", "dest")]
    [InlineData("ext::sh -c touch% /tmp/pwned", "dest")]
    [InlineData("https://example.com/repo.git", "dest")]
    [InlineData("ssh://git@host/repo", "../evil")]
    public void Fetch_Rejects_Unsafe_Input(string url, string remote)
    {
        var fetch = () => new GitRepositoryService(_git).FetchAsync(Path.GetTempPath(), url, remote, null);
        fetch.Should().ThrowAsync<ArgumentException>().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Verifies that a failed fetch surfaces git's error.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Fetch_Failure_Throws()
    {
        _git.RunAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<string?>(), Arg.Any<IReadOnlyDictionary<string, string>?>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(128, "", "fatal: Could not read from remote repository."));

        var fetch = () => new GitRepositoryService(_git).FetchAsync(Path.GetTempPath(), "ssh://git@h:22/r", "dest", null);

        (await fetch.Should().ThrowAsync<GitExecutionException>()).Which.ExitCode.Should().Be(128);
    }

    /// <summary>
    /// Verifies that repository paths with shell metacharacters are rejected when building ssh URLs.
    /// </summary>
    [Fact]
    public void BuildSshUrl_Rejects_Unsafe_Path_And_Handles_Windows()
    {
        var endpoint = new SshEndpoint("git", "::1");
        GitRepositoryService.BuildSshUrl(endpoint, "C:\\repos\\app").Should().Be("ssh://git@[::1]:22/C:/repos/app");
        var unsafePath = () => GitRepositoryService.BuildSshUrl(endpoint, "/srv/repo'; rm -rf /");
        unsafePath.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// Configures a response for an exact argument list.
    /// </summary>
    /// <param name="arguments">The space-separated arguments.</param>
    /// <param name="exitCode">The exit code.</param>
    /// <param name="stdout">The standard output.</param>
    private void Setup(string arguments, int exitCode, string stdout)
    {
        var expected = arguments.Split(' ');
        _git.RunAsync(Arg.Is<IReadOnlyList<string>>(args => args.SequenceEqual(expected)), Arg.Any<string?>(), Arg.Any<IReadOnlyDictionary<string, string>?>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(exitCode, stdout, ""));
    }
}
