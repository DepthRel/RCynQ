using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Api;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Ids;
using RCynQ.Git.Commands;
using RCynQ.Git.Processes;
using RCynQ.Service.Tests.Support;
using RCynQ.Sync.Paths;
using RCynQ.Sync.Services;

namespace RCynQ.Service.Tests;

/// <summary>
/// End-to-end tests of the repositories of a running node: the local API, what a neighbour may ask, and the rule that
/// the repository of an Origin is only ever read. They return early when git is not installed.
/// </summary>
public sealed class RepositoriesEndToEndTests
{
    /// <summary>JSON options matching the service.</summary>
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    /// <summary>The Git command runner.</summary>
    private readonly GitCommandRunner _git = new(new SystemProcessRunner(TestTexts.English), new GitOptions());

    /// <summary>
    /// Verifies that an Origin lists, registers, switches and removes repositories through its local API without a
    /// restart, and stores them in its configuration file.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Origin_Registers_Switches_And_Removes_Repositories()
    {
        if (!await GitAvailableAsync())
        {
            return;
        }

        await using var host = await ServiceTestHost.StartAsync(NodeRole.Origin);
        var app = Path.Combine(host.Root, "work");
        var tools = Path.Combine(host.Root, "tools");
        await InitAsync(app, "app");
        await InitAsync(tools, "tools");
        var client = host.CreateClient();

        (await host.CreateClient(withApiToken: false).GetAsync(ApiRoutes.REPOSITORIES)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var initial = await client.GetFromJsonAsync<RepositoryListDto>(ApiRoutes.REPOSITORIES, _json);
        initial!.Active.Should().Be(ServiceTestHost.REPOSITORY_NAME);
        initial.Repositories.Should().ContainSingle().Which.Should().BeEquivalentTo(new RepositoryDto(ServiceTestHost.REPOSITORY_NAME, app, true, true, true));

        var notGit = await client.PostAsJsonAsync(ApiRoutes.REPOSITORIES, new AddRepositoryRequest("plain", Directory.CreateDirectory(Path.Combine(host.Root, "plain")).FullName), _json);
        notGit.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await notGit.Content.ReadFromJsonAsync<ErrorResponse>(_json))!.Error.Should().Contain("not a Git repository");

        var added = await (await client.PostAsJsonAsync(ApiRoutes.REPOSITORIES, new AddRepositoryRequest("tools", tools), _json)).Content.ReadFromJsonAsync<RepositoryListDto>(_json);
        added!.Repositories.Select(repository => repository.Name).Should().Equal(ServiceTestHost.REPOSITORY_NAME, "tools");
        added.Active.Should().Be(ServiceTestHost.REPOSITORY_NAME);

        var removeActive = await client.DeleteAsync(ApiRoutes.RepositoryPath(ServiceTestHost.REPOSITORY_NAME));
        removeActive.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var switched = await (await client.PostAsync(ApiRoutes.RepositoryActivatePath("tools"), null)).Content.ReadFromJsonAsync<RepositoryListDto>(_json);
        switched!.Active.Should().Be("tools");
        host.Services.GetRequiredService<SyncPaths>().Workdir.Should().Be(tools, "the switch takes effect without a restart");
        (await client.GetFromJsonAsync<NodeConfigurationDto>(ApiRoutes.CONFIG, _json))!.RepoPath.Should().Be(tools);
        (await client.GetFromJsonAsync<JsonElement>(ApiRoutes.GIT_BRANCH, _json)).GetProperty("branch").GetString().Should().Be("tools-branch");

        var unknown = await client.PostAsync(ApiRoutes.RepositoryActivatePath("gone"), null);
        unknown.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var removed = await (await client.DeleteAsync(ApiRoutes.RepositoryPath(ServiceTestHost.REPOSITORY_NAME))).Content.ReadFromJsonAsync<RepositoryListDto>(_json);
        removed!.Repositories.Should().ContainSingle().Which.Name.Should().Be("tools");
        File.Exists(Path.Combine(app, "readme.txt")).Should().BeTrue("removing a repository from the list leaves its files alone");
    }

    /// <summary>
    /// Verifies that a neighbour learns the names of the repositories of an Origin only with the link token, never their
    /// paths, and that a Workstation is not asked at all.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Neighbour_Is_Told_The_Names_Of_The_Repositories_Only()
    {
        await using var host = await ServiceTestHost.StartAsync(NodeRole.Origin);
        using (var scope = host.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IPeerRepository>().UpsertAsync(
                new PeerDescriptor(new NodeId("workstation-1"), "Workstation", NodeRole.Workstation, "workstation.local", 22, "rcynq", signingPublicKey: Convert.ToBase64String(new byte[] { 1, 2, 3 })));
        }

        var anonymous = host.CreateClient(withApiToken: false);
        (await anonymous.GetAsync(ApiRoutes.PEER_REPOSITORIES)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the link token is missing");

        using var wrongToken = Request("workstation-1", "wrong-token");
        (await anonymous.SendAsync(wrongToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var stranger = Request("stranger-1", ServiceTestHost.LINK_TOKEN);
        (await anonymous.SendAsync(stranger)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var request = Request("workstation-1", ServiceTestHost.LINK_TOKEN);
        var response = await anonymous.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var text = await response.Content.ReadAsStringAsync();
        var answer = JsonSerializer.Deserialize<PeerRepositoriesResponse>(text, _json)!;
        answer.Names.Should().Equal(ServiceTestHost.REPOSITORY_NAME);
        answer.Active.Should().Be(ServiceTestHost.REPOSITORY_NAME);
        text.Should().NotContain("work", "the path of a repository stays on the node that owns it");
    }

    /// <summary>
    /// Verifies the rule the application is built on: whatever an Origin does with a repository (registering it, looking
    /// at its state, switching to it and away from it, serving its branches), nothing inside its <c>.git</c> changes.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Repository_Of_An_Origin_Is_Never_Written_To()
    {
        if (!await GitAvailableAsync())
        {
            return;
        }

        await using var host = await ServiceTestHost.StartAsync(NodeRole.Origin);
        var app = Path.Combine(host.Root, "work");
        var tools = Path.Combine(host.Root, "tools");
        await InitAsync(app, "app");
        await InitAsync(tools, "tools");

        // An edit that is not committed: a status that refreshed the index would show here.
        await File.WriteAllTextAsync(Path.Combine(app, "readme.txt"), "edited\n");
        await File.WriteAllTextAsync(Path.Combine(tools, "new.txt"), "untracked\n");
        using (var scope = host.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IPeerRepository>().UpsertAsync(
                new PeerDescriptor(new NodeId("workstation-1"), "Workstation", NodeRole.Workstation, "workstation.local", 22, "rcynq", signingPublicKey: Convert.ToBase64String(new byte[] { 1, 2, 3 })));
        }

        var appBefore = Fingerprint(Path.Combine(app, ".git"));
        var toolsBefore = Fingerprint(Path.Combine(tools, ".git"));
        var client = host.CreateClient();
        var patches = host.Services.GetRequiredService<IPatchService>();

        await patches.GetStateAsync();
        (await client.PostAsJsonAsync(ApiRoutes.REPOSITORIES, new AddRepositoryRequest("tools", tools), _json)).EnsureSuccessStatusCode();
        await BundleAsync(host, ServiceTestHost.REPOSITORY_NAME);
        await BundleAsync(host, "tools");
        (await client.PostAsync(ApiRoutes.RepositoryActivatePath("tools"), null)).EnsureSuccessStatusCode();
        await patches.GetStateAsync();
        (await client.GetAsync(ApiRoutes.GIT_BRANCH)).EnsureSuccessStatusCode();
        (await client.PostAsync(ApiRoutes.DIAGNOSTICS_RUN, null)).EnsureSuccessStatusCode();
        (await client.PostAsync(ApiRoutes.RepositoryActivatePath(ServiceTestHost.REPOSITORY_NAME), null)).EnsureSuccessStatusCode();
        await patches.GetStateAsync();
        (await client.DeleteAsync(ApiRoutes.RepositoryPath("tools"))).EnsureSuccessStatusCode();

        Fingerprint(Path.Combine(app, ".git")).Should().Equal(appBefore, "the repository of an Origin is only read");
        Fingerprint(Path.Combine(tools, ".git")).Should().Equal(toolsBefore, "the repository of an Origin is only read");
        (await File.ReadAllTextAsync(Path.Combine(app, "readme.txt"))).Should().Be("edited\n");
        (await File.ReadAllTextAsync(Path.Combine(tools, "new.txt"))).Should().Be("untracked\n");
    }

    /// <summary>
    /// Asks the node for a bundle of a repository the way a Workstation does and reads it to the end.
    /// </summary>
    /// <param name="host">The node.</param>
    /// <param name="repository">The repository name.</param>
    /// <returns>A task representing the request.</returns>
    private static async Task BundleAsync(ServiceTestHost host, string repository)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, ApiRoutes.GIT_BUNDLE)
        {
            Content = JsonContent.Create(new GitBundleRequest(Array.Empty<string>(), repository), options: _json)
        };
        request.Headers.Add(ApiHeaders.NODE_ID, "workstation-1");
        request.Headers.Add(ApiHeaders.LINK_TOKEN, ServiceTestHost.LINK_TOKEN);
        using var response = await host.CreateClient(withApiToken: false).SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await response.Content.ReadAsByteArrayAsync()).Should().NotBeEmpty();
    }

    /// <summary>
    /// Creates the request of a neighbour for the names of the repositories.
    /// </summary>
    /// <param name="nodeId">The identifier the neighbour gives.</param>
    /// <param name="token">The link token it gives.</param>
    /// <returns>The request.</returns>
    private static HttpRequestMessage Request(string nodeId, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, ApiRoutes.PEER_REPOSITORIES);
        request.Headers.Add(ApiHeaders.NODE_ID, nodeId);
        request.Headers.Add(ApiHeaders.LINK_TOKEN, token);
        return request;
    }

    /// <summary>
    /// Creates a Git repository with one commit on a branch named after the repository.
    /// </summary>
    /// <param name="directory">The directory.</param>
    /// <param name="name">The repository name.</param>
    /// <returns>A task representing the operation.</returns>
    private async Task InitAsync(string directory, string name)
    {
        Directory.CreateDirectory(directory);
        await GitAsync(directory, "init", "--quiet", $"--initial-branch={name}-branch");
        await File.WriteAllTextAsync(Path.Combine(directory, "readme.txt"), name + "\n");
        await GitAsync(directory, "add", "-A");
        await GitAsync(directory, "-c", "user.name=t", "-c", "user.email=t@t", "commit", "--quiet", "-m", "first");
    }

    /// <summary>
    /// Runs git and expects it to succeed.
    /// </summary>
    /// <param name="directory">The working directory.</param>
    /// <param name="arguments">The arguments.</param>
    /// <returns>A task representing the operation.</returns>
    private async Task GitAsync(string directory, params string[] arguments)
    {
        var result = await _git.RunAsync(arguments, directory);
        result.ExitCode.Should().Be(0, $"git {string.Join(' ', arguments)}: {result.StandardError}");
    }

    /// <summary>
    /// Tells whether git is installed.
    /// </summary>
    /// <returns><see langword="true"/> when git runs.</returns>
    private async Task<bool> GitAvailableAsync()
    {
        try
        {
            return (await _git.RunAsync(new[] { "--version" }, Path.GetTempPath())).ExitCode == 0;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception or RCynQ.Core.Exceptions.SyncBridgeException)
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
}
