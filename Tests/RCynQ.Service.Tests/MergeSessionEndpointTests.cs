using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using RCynQ.Core.Api;
using RCynQ.Core.Enums;
using RCynQ.Service.Tests.Support;

namespace RCynQ.Service.Tests;

/// <summary>
/// Tests of the merge session endpoints on a real service: the routes, the API token and the mapping of errors. What a
/// session does with the files is tested in <c>RCynQ.Sync.Tests</c>.
/// </summary>
public sealed class MergeSessionEndpointTests
{
    /// <summary>The JSON options of the API.</summary>
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Verifies the answers for a patch and a session that do not exist, and that giving up is always answered.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Unknown_Patches_And_Sessions_Are_Answered()
    {
        await using var host = await ServiceTestHost.StartAsync(NodeRole.Origin);
        var client = host.CreateClient();

        (await client.PostAsync(ApiRoutes.PatchMergeSessionsPath(Guid.NewGuid()), null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var complete = await client.PostAsJsonAsync(ApiRoutes.MergeSessionCompletePath(Guid.NewGuid()), new CompleteMergeSessionRequest("KDiff3"), _json);
        complete.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await complete.Content.ReadFromJsonAsync<ErrorResponse>(_json))!.Error.Should().NotBeNullOrWhiteSpace();
        (await client.DeleteAsync(ApiRoutes.MergeSessionPath(Guid.NewGuid()))).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    /// <summary>
    /// Verifies that the name of the tool is checked before anything else.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task A_Bad_Tool_Name_Is_Refused()
    {
        await using var host = await ServiceTestHost.StartAsync(NodeRole.Origin);

        var response = await host.CreateClient().PostAsJsonAsync(ApiRoutes.MergeSessionCompletePath(Guid.NewGuid()), new CompleteMergeSessionRequest(" "), _json);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Verifies that the endpoints need the API token: a peer arriving through the SSH forward must not merge.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task The_Endpoints_Need_The_Api_Token()
    {
        await using var host = await ServiceTestHost.StartAsync(NodeRole.Origin);
        var peer = host.CreateClient(withApiToken: false);

        (await peer.PostAsync(ApiRoutes.PatchMergeSessionsPath(Guid.NewGuid()), null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await peer.PostAsJsonAsync(ApiRoutes.MergeSessionCompletePath(Guid.NewGuid()), new CompleteMergeSessionRequest("Meld"), _json))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await peer.DeleteAsync(ApiRoutes.MergeSessionPath(Guid.NewGuid()))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Verifies that a Relay, which receives no patches, has nothing to merge.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task A_Relay_Has_Nothing_To_Merge()
    {
        await using var host = await ServiceTestHost.StartAsync(NodeRole.Relay);

        (await host.CreateClient().PostAsync(ApiRoutes.PatchMergeSessionsPath(Guid.NewGuid()), null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
