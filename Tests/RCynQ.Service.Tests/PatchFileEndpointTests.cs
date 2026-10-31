using System.Net;
using System.Net.Http.Json;
using System.Text;
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
/// Tests of the endpoints of patch files on a real service and a real repository. The details of patches are tested in
/// <c>RCynQ.Git.Tests</c> and <c>RCynQ.Sync.Tests</c>.
/// </summary>
public sealed class PatchFileEndpointTests
{
    /// <summary>The JSON options of the API.</summary>
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    /// <summary>The Git runner.</summary>
    private readonly GitCommandRunner _git = new(new SystemProcessRunner(TestTexts.English), new GitOptions());

    /// <summary>
    /// Verifies the whole way of a patch through the API on an Origin: saved, taken out, listed and brought back in part.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task A_Patch_Goes_Out_And_Comes_Back_Through_The_Api()
    {
        await using var host = await ServiceTestHost.StartAsync(NodeRole.Origin);
        var work = await CreateRepositoryAsync(host);
        var client = host.CreateClient();
        await File.WriteAllTextAsync(Path.Combine(work, "a.txt"), "two\n");
        await File.WriteAllTextAsync(Path.Combine(work, "new.txt"), "new\n");

        var built = await client.PostAsJsonAsync(ApiRoutes.PATCH_FILE, new BuildPatchFileRequest(null), _json);
        built.StatusCode.Should().Be(HttpStatusCode.OK);
        var patch = await built.Content.ReadAsByteArrayAsync();
        Encoding.UTF8.GetString(patch).Should().Contain("a/a.txt").And.Contain("b/new.txt");

        var removed = await PostApplyAsync(client, new ApplyPatchFileRequest(patch, null, true));
        removed.Succeeded.Should().BeTrue(removed.Output);
        File.Exists(Path.Combine(work, "new.txt")).Should().BeFalse();

        var read = await client.PostAsJsonAsync(ApiRoutes.PATCH_FILE_READ, new ReadPatchFileRequest(patch), _json);
        (await read.Content.ReadFromJsonAsync<ChangedFile[]>(_json))!.Select(file => file.Path).Should().BeEquivalentTo("a.txt", "new.txt");

        var applied = await PostApplyAsync(client, new ApplyPatchFileRequest(patch, new[] { "new.txt" }, false));
        applied.Succeeded.Should().BeTrue(applied.Output);
        File.Exists(Path.Combine(work, "new.txt")).Should().BeTrue();
        (await File.ReadAllTextAsync(Path.Combine(work, "a.txt"))).ReplaceLineEndings("\n").Should().Be("one\n");
    }

    /// <summary>
    /// Verifies that a choice of files narrows the saved patch.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task A_Saved_Patch_Holds_Only_The_Named_Files()
    {
        await using var host = await ServiceTestHost.StartAsync(NodeRole.Origin);
        var work = await CreateRepositoryAsync(host);
        await File.WriteAllTextAsync(Path.Combine(work, "a.txt"), "two\n");
        await File.WriteAllTextAsync(Path.Combine(work, "new.txt"), "new\n");

        var built = await host.CreateClient().PostAsJsonAsync(ApiRoutes.PATCH_FILE, new BuildPatchFileRequest(new[] { "new.txt" }), _json);

        Encoding.UTF8.GetString(await built.Content.ReadAsByteArrayAsync()).Should().Contain("b/new.txt").And.NotContain("a/a.txt");
    }

    /// <summary>
    /// Verifies that a file without a patch gets a clear answer with its reason.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task A_File_Without_A_Patch_Is_Answered_With_The_Reason()
    {
        await using var host = await ServiceTestHost.StartAsync(NodeRole.Origin);
        await CreateRepositoryAsync(host);

        var response = await host.CreateClient().PostAsJsonAsync(ApiRoutes.PATCH_FILE_READ, new ReadPatchFileRequest("no patch here\n"u8.ToArray()), _json);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ErrorResponse>(_json))!.Error.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// Verifies that a request without the content of the patch is refused, not crashed on.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task A_Request_Without_Content_Is_Refused()
    {
        await using var host = await ServiceTestHost.StartAsync(NodeRole.Origin);

        var response = await host.CreateClient().PostAsJsonAsync(ApiRoutes.PATCH_FILE_APPLY, new { paths = (string[]?)null, reverse = false }, _json);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Verifies that the endpoints need the API token: a peer arriving through the SSH forward must not change the working tree.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task The_Endpoints_Need_The_Api_Token()
    {
        await using var host = await ServiceTestHost.StartAsync(NodeRole.Origin);
        var peer = host.CreateClient(withApiToken: false);

        (await peer.PostAsJsonAsync(ApiRoutes.PATCH_FILE, new BuildPatchFileRequest(null), _json)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await peer.PostAsJsonAsync(ApiRoutes.PATCH_FILE_READ, new ReadPatchFileRequest(new byte[] { 1 }), _json)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await peer.PostAsJsonAsync(ApiRoutes.PATCH_FILE_APPLY, new ApplyPatchFileRequest(new byte[] { 1 }, null, false), _json)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Verifies that a Relay, which has no working tree, has no patch files.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task A_Relay_Has_No_Patch_Files()
    {
        await using var host = await ServiceTestHost.StartAsync(NodeRole.Relay);
        var client = host.CreateClient();

        (await client.PostAsJsonAsync(ApiRoutes.PATCH_FILE, new BuildPatchFileRequest(null), _json)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.PostAsJsonAsync(ApiRoutes.PATCH_FILE_READ, new ReadPatchFileRequest(new byte[] { 1 }), _json)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.PostAsJsonAsync(ApiRoutes.PATCH_FILE_APPLY, new ApplyPatchFileRequest(new byte[] { 1 }, null, false), _json)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Posts a patch to apply and reads the answer.
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="request">The request.</param>
    /// <returns>The answer.</returns>
    private static async Task<PatchFileApplyResult> PostApplyAsync(HttpClient client, ApplyPatchFileRequest request)
    {
        var response = await client.PostAsJsonAsync(ApiRoutes.PATCH_FILE_APPLY, request, _json);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<PatchFileApplyResult>(_json))!;
    }

    /// <summary>
    /// Makes the working tree of a hosted node a repository with one commit.
    /// </summary>
    /// <param name="host">The host.</param>
    /// <returns>The working tree.</returns>
    private async Task<string> CreateRepositoryAsync(ServiceTestHost host)
    {
        var work = Path.Combine(host.Root, "work");
        await File.WriteAllTextAsync(Path.Combine(work, "a.txt"), "one\n");
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
