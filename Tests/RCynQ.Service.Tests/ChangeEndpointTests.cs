using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using RCynQ.Core.Api;
using RCynQ.Core.Enums;
using RCynQ.Core.State;
using RCynQ.Git.Commands;
using RCynQ.Git.Processes;
using RCynQ.Service.Tests.Support;

namespace RCynQ.Service.Tests;

/// <summary>
/// Tests of the endpoints of the changed files on a real service and a real repository: the diff of a file and the
/// choice of its changes. The details are tested in <c>RCynQ.Sync.Tests</c>.
/// </summary>
public sealed class ChangeEndpointTests
{
    /// <summary>The JSON options of the API.</summary>
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    /// <summary>The Git runner.</summary>
    private readonly GitCommandRunner _git = new(new SystemProcessRunner(TestTexts.English), new GitOptions());

    /// <summary>
    /// Verifies the way of a choice through the API: the diff, a part chosen, the list marks the file, the whole file
    /// again.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task A_Part_Is_Chosen_Through_The_Api()
    {
        await using var host = await ServiceTestHost.StartAsync(NodeRole.Origin);
        var work = await CreateRepositoryAsync(host);
        var client = host.CreateClient();
        await File.WriteAllTextAsync(Path.Combine(work, "a.txt"), "1\nTWO\n3\n4\n5\n6\n7\n8\nNINE\n");
        (await client.GetAsync(ApiRoutes.STATE)).StatusCode.Should().Be(HttpStatusCode.OK);

        var diff = (await client.GetFromJsonAsync<FileDiffResponse>(ApiRoutes.ChangesDiffPath("a.txt"), _json))!;
        diff.Blocks.Should().HaveCount(2);

        var chosen = await client.PutAsJsonAsync(ApiRoutes.CHANGES_PARTIAL, new ChooseChangesRequest("a.txt", diff.BaseSha256, diff.WorkdirSha256, new[] { 1 }), _json);
        chosen.StatusCode.Should().Be(HttpStatusCode.OK);
        (await chosen.Content.ReadFromJsonAsync<PartialChoiceResult>(_json))!.Outcome.Should().Be(PartialChoiceOutcome.Partial);
        (await StateOfAsync(client, "a.txt")).Partial.Should().Be(PartialChangeState.Partial);

        var stale = await client.PutAsJsonAsync(ApiRoutes.CHANGES_PARTIAL, new ChooseChangesRequest("a.txt", diff.BaseSha256, "0000", new[] { 1 }), _json);
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);

        (await client.DeleteAsync(ApiRoutes.ChangesPartialPath("a.txt"))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await StateOfAsync(client, "a.txt")).Partial.Should().Be(PartialChangeState.None);
    }

    /// <summary>
    /// Verifies that the files for an external tool are prepared, and that a path out of the working tree is refused.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Compare_Prepares_Files_And_Refuses_Foreign_Paths()
    {
        await using var host = await ServiceTestHost.StartAsync(NodeRole.Origin);
        var work = await CreateRepositoryAsync(host);
        var client = host.CreateClient();
        await File.WriteAllTextAsync(Path.Combine(work, "a.txt"), "changed\n");
        await client.GetAsync(ApiRoutes.STATE);

        var compare = await client.PostAsJsonAsync(ApiRoutes.CHANGES_COMPARE, new CompareFilesRequest("a.txt", ForChoice: false), _json);
        var files = (await compare.Content.ReadFromJsonAsync<CompareFilesResponse>(_json))!;

        (await File.ReadAllTextAsync(files.BaseFile)).Should().Be("1\n2\n3\n4\n5\n6\n7\n8\n9\n");
        Path.GetFullPath(files.WorkFile).Should().Be(Path.GetFullPath(Path.Combine(work, "a.txt")));
        (await client.GetAsync(ApiRoutes.ChangesDiffPath("../outside.txt"))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Verifies that a Workstation takes the changes of a file back through the API and that the file leaves the list,
    /// and that an Origin refuses to.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task A_Workstation_Reverts_Through_The_Api_And_An_Origin_Refuses()
    {
        await using var host = await ServiceTestHost.StartAsync(NodeRole.Workstation);
        var work = await CreateRepositoryAsync(host);
        var client = host.CreateClient();
        await File.WriteAllTextAsync(Path.Combine(work, "a.txt"), "changed\n");

        var reverted = await client.PostAsJsonAsync(ApiRoutes.CHANGES_REVERT, new RevertChangesRequest("a.txt"), _json);

        reverted.StatusCode.Should().Be(HttpStatusCode.OK);
        (await reverted.Content.ReadFromJsonAsync<RevertChangesResult>(_json))!.Outcome.Should().Be(WorkingTreeRevertOutcome.Restored);
        (await File.ReadAllTextAsync(Path.Combine(work, "a.txt"))).Should().Be("1\n2\n3\n4\n5\n6\n7\n8\n9\n");
        (await client.PostAsJsonAsync(ApiRoutes.CHANGES_REVERT, new RevertChangesRequest("../outside.txt"), _json)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        await using var origin = await ServiceTestHost.StartAsync(NodeRole.Origin);
        var refused = await origin.CreateClient().PostAsJsonAsync(ApiRoutes.CHANGES_REVERT, new RevertChangesRequest("a.txt"), _json);
        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Verifies that the endpoints need the API token, and that a Relay has none of them.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task The_Endpoints_Need_The_Token_And_A_Working_Tree()
    {
        await using var origin = await ServiceTestHost.StartAsync(NodeRole.Origin);
        (await origin.CreateClient(withApiToken: false).GetAsync(ApiRoutes.ChangesDiffPath("a.txt"))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        await using var relay = await ServiceTestHost.StartAsync(NodeRole.Relay);
        (await relay.CreateClient().GetAsync(ApiRoutes.ChangesDiffPath("a.txt"))).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Reads the state and finds a changed file in it.
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="path">The path of the file.</param>
    /// <returns>The file.</returns>
    private static async Task<ChangedFile> StateOfAsync(HttpClient client, string path)
    {
        var state = (await client.GetFromJsonAsync<NodeSyncStatus>(ApiRoutes.STATE, _json))!;
        return state.ChangedFiles!.Single(file => file.Path == path);
    }

    /// <summary>
    /// Makes the working tree of a hosted node a repository with one commit.
    /// </summary>
    /// <param name="host">The host.</param>
    /// <returns>The working tree.</returns>
    private async Task<string> CreateRepositoryAsync(ServiceTestHost host)
    {
        var work = Path.Combine(host.Root, "work");
        await File.WriteAllTextAsync(Path.Combine(work, "a.txt"), "1\n2\n3\n4\n5\n6\n7\n8\n9\n");
        await RunGitAsync(work, "init", "--quiet", "--initial-branch=main");
        await RunGitAsync(work, "config", "core.autocrlf", "false");
        await RunGitAsync(work, "add", "-A");
        await RunGitAsync(work, "-c", "user.name=t", "-c", "user.email=t@t", "commit", "--quiet", "-m", "base");
        return work;
    }

    /// <summary>
    /// Runs git and fails the test when git fails.
    /// </summary>
    /// <param name="directory">The working directory.</param>
    /// <param name="arguments">The arguments.</param>
    /// <returns>A task representing the command.</returns>
    private async Task RunGitAsync(string directory, params string[] arguments)
    {
        var result = await _git.RunInWorkingTreeAsync(arguments, directory);
        result.ExitCode.Should().Be(0, result.StandardError);
    }
}
