using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using RCynQ.Core.Abstractions.Events;
using RCynQ.Core.Api;
using RCynQ.Core.Config;
using RCynQ.Core.Diagnostics;
using RCynQ.Core.Enums;
using RCynQ.Core.Events;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Ids;
using RCynQ.Core.Patches;
using RCynQ.Core.State;
using RCynQ.Git.Patches.Serialization;
using RCynQ.Sync.Transfer;
using RCynQ.Service.Tests.Support;
using RCynQ.Sync.Configuration;
using RCynQ.Sync.Diagnostics.AutoFix;
using Microsoft.Extensions.Hosting;
using RCynQ.Sync.Configuration.Probing;
using RCynQ.Sync.Services;

namespace RCynQ.Service.Tests;

/// <summary>
/// Integration tests of the service API, security and SignalR on a test server with a mocked patch service.
/// </summary>
public sealed class ServiceApiTests
{
    /// <summary>
    /// JSON options matching the service.
    /// </summary>
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Verifies that health is public and management endpoints require the local API token.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Health_Is_Public_And_Management_Requires_Token()
    {
        var patches = Substitute.For<IPatchService>();
        patches.GetStateAsync(Arg.Any<CancellationToken>()).Returns(new NodeSyncStatus(new NodeId("dest-1"), "dest-1", NodeRole.Dest, new SyncState("a", "b", null, null), new SyncBaseStatus(SyncBaseMode.Git, "main", new string('d', 40), null, false), Array.Empty<FileChange>(), 0));
        await using var host = await ServiceTestHost.StartAsync(NodeRole.Dest, services => Replace(services, patches));

        (await host.CreateClient(withApiToken: false).GetAsync(ApiRoutes.Health)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await host.CreateClient(withApiToken: false).GetAsync(ApiRoutes.State)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var wrongToken = host.CreateClient(withApiToken: false);
        wrongToken.DefaultRequestHeaders.Add(ApiHeaders.ApiToken, "guess");
        (await wrongToken.GetAsync(ApiRoutes.State)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var state = await host.CreateClient().GetFromJsonAsync<NodeSyncStatus>(ApiRoutes.State, Json);
        state!.NodeId.Should().Be(new NodeId("dest-1"));
        File.ReadAllText(Path.Combine(host.Root, "data", "keys", "api.token")).Trim().Should().Be(host.ApiToken);
    }

    /// <summary>
    /// Verifies that the local API token file exists right after startup, before any request.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Api_Token_File_Is_Created_On_Startup()
    {
        await using var host = await ServiceTestHost.StartAsync(NodeRole.Mediator, services => Replace(services, Substitute.For<IPatchService>()));

        var tokenFile = Path.Combine(host.Root, "data", "keys", "api.token");
        File.Exists(tokenFile).Should().BeTrue();
        File.ReadAllText(tokenFile).Trim().Should().HaveLength(43);
    }

    /// <summary>
    /// Verifies that malformed envelopes are refused with the Malformed reason.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Patch_Malformed_Returns_400()
    {
        await using var host = await ServiceTestHost.StartAsync(NodeRole.Dest, services => Replace(services, Substitute.For<IPatchService>()));

        var response = await host.CreateClient(withApiToken: false).PostAsync(ApiRoutes.PatchSessions, new StringContent("{not json", System.Text.Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<PatchRejectionResponse>(Json))!.Reason.Should().Be(PatchRejectionReason.Malformed);
    }

    /// <summary>
    /// Verifies that a body announced as gzip but not valid gzip data is refused as malformed.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Patch_Corrupt_Gzip_Returns_400()
    {
        await using var host = await ServiceTestHost.StartAsync(NodeRole.Dest, services => Replace(services, Substitute.For<IPatchService>()));
        using var request = new HttpRequestMessage(HttpMethod.Post, ApiRoutes.PatchSessions) { Content = new ByteArrayContent("this is not gzip"u8.ToArray()) };
        request.Content.Headers.ContentEncoding.Add("gzip");

        var response = await host.CreateClient(withApiToken: false).SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<PatchRejectionResponse>(Json))!.Reason.Should().Be(PatchRejectionReason.Malformed);
    }

    /// <summary>
    /// Verifies the mapping of rejections and that headers reach the receiver.
    /// </summary>
    /// <param name="reason">The rejection reason.</param>
    /// <param name="status">The expected status code.</param>
    /// <returns>A task representing the test.</returns>
    [Theory]
    [InlineData(PatchRejectionReason.InvalidSignature, HttpStatusCode.Forbidden)]
    [InlineData(PatchRejectionReason.InvalidToken, HttpStatusCode.Unauthorized)]
    [InlineData(PatchRejectionReason.Replay, HttpStatusCode.Conflict)]
    public async Task Patch_Rejections_Map_To_Status(PatchRejectionReason reason, HttpStatusCode status)
    {
        var patches = Substitute.For<IPatchService>();
        patches.OpenSessionAsync(Arg.Any<PatchEnvelope>(), Arg.Any<PatchReceiveContext>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new PatchRejectedException(reason, "refused"));
        patches.CommitAsync(Arg.Any<Guid>(), Arg.Any<PatchReceiveContext>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new PatchRejectedException(reason, "refused"));
        await using var host = await ServiceTestHost.StartAsync(NodeRole.Dest, services => Replace(services, patches));

        var response = await PostEnvelopeAsync(host, "source-1", "presented-token");
        var commit = await PostCommitAsync(host, Guid.NewGuid(), "source-1", "presented-token");

        response.StatusCode.Should().Be(status);
        commit.StatusCode.Should().Be(status);
        (await response.Content.ReadFromJsonAsync<PatchRejectionResponse>(Json))!.Reason.Should().Be(reason);
        await patches.Received(1).OpenSessionAsync(Arg.Any<PatchEnvelope>(), new PatchReceiveContext("source-1", "presented-token"), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies the three routes of a session without the local API token: the manifest arrives decompressed, the content
    /// arrives decompressed as a stream, and the commit returns the result.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Session_Routes_Open_Receive_Content_And_Commit()
    {
        var patches = Substitute.For<IPatchService>();
        PatchEnvelope? opened = null;
        patches.OpenSessionAsync(Arg.Any<PatchEnvelope>(), Arg.Any<PatchReceiveContext>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                opened = call.Arg<PatchEnvelope>();
                return new PatchSession(opened.Id, new[] { new string('b', 64) });
            });
        byte[]? received = null;
        patches.ReceiveContentAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<PatchReceiveContext>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                using var copy = new MemoryStream();
                await call.Arg<Stream>().CopyToAsync(copy);
                received = copy.ToArray();
            });
        patches.CommitAsync(Arg.Any<Guid>(), Arg.Any<PatchReceiveContext>(), Arg.Any<CancellationToken>())
            .Returns(call => PatchApplyResult.Applied(call.Arg<Guid>(), 2));
        await using var host = await ServiceTestHost.StartAsync(NodeRole.Dest, services => Replace(services, patches));

        var open = await PostEnvelopeAsync(host, "source-1", ServiceTestHost.LinkToken);
        open.StatusCode.Should().Be(HttpStatusCode.OK);
        var session = await open.Content.ReadFromJsonAsync<PatchSession>(Json);
        session!.PatchId.Should().Be(opened!.Id);
        session.MissingContent.Should().Equal(new string('b', 64));

        var content = string.Concat(Enumerable.Repeat("streamed content\n", 50_000));
        var put = await PutContentAsync(host, opened.Id, new string('b', 64), content, "source-1", ServiceTestHost.LinkToken);
        put.StatusCode.Should().Be(HttpStatusCode.NoContent);
        System.Text.Encoding.UTF8.GetString(received!).Should().Be(content, "the service decompresses the body before handing it over");
        await patches.Received(1).ReceiveContentAsync(opened.Id, new string('b', 64), Arg.Any<Stream>(), new PatchReceiveContext("source-1", ServiceTestHost.LinkToken), Arg.Any<CancellationToken>());

        var commit = await PostCommitAsync(host, opened.Id, "source-1", ServiceTestHost.LinkToken);
        commit.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await commit.Content.ReadFromJsonAsync<PatchApplyResult>(Json);
        result!.Status.Should().Be(PatchStatus.Applied);
        result.AppliedFileCount.Should().Be(2);
    }

    /// <summary>
    /// Verifies that a content upload refused by the receiver is answered with the rejection reason.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Refused_Content_Returns_The_Rejection()
    {
        var patches = Substitute.For<IPatchService>();
        patches.ReceiveContentAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<PatchReceiveContext>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new PatchRejectedException(PatchRejectionReason.Malformed, "hash mismatch"));
        await using var host = await ServiceTestHost.StartAsync(NodeRole.Dest, services => Replace(services, patches));

        var response = await PutContentAsync(host, Guid.NewGuid(), new string('c', 64), "x", "source-1", ServiceTestHost.LinkToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<PatchRejectionResponse>(Json))!.Error.Should().Contain("hash mismatch");
    }

    /// <summary>
    /// Verifies the resolve endpoint and its error mapping.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Resolve_Endpoint_Calls_Service_And_Maps_Errors()
    {
        var known = Guid.NewGuid();
        var patches = Substitute.For<IPatchService>();
        patches.ResolveAsync(known, ConflictPolicy.ApplyIncomingOverLocal, Arg.Any<CancellationToken>())
            .Returns(new PatchApplyResult(known, PatchStatus.ConflictResolved, 1, Array.Empty<ConflictFile>(), null));
        patches.ResolveAsync(Arg.Is<Guid>(id => id != known), Arg.Any<ConflictPolicy>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new KeyNotFoundException("missing"));
        await using var host = await ServiceTestHost.StartAsync(NodeRole.Dest, services => Replace(services, patches));
        var client = host.CreateClient();

        var ok = await client.PostAsJsonAsync(ApiRoutes.ResolvePatchPath(known), new ResolvePatchRequest(ConflictPolicy.ApplyIncomingOverLocal), Json);
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ok.Content.ReadFromJsonAsync<PatchApplyResult>(Json))!.Status.Should().Be(PatchStatus.ConflictResolved);

        (await client.PostAsJsonAsync(ApiRoutes.ResolvePatchPath(Guid.NewGuid()), new ResolvePatchRequest(ConflictPolicy.KeepLocal), Json))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await host.CreateClient(withApiToken: false).PostAsJsonAsync(ApiRoutes.ResolvePatchPath(known), new ResolvePatchRequest(ConflictPolicy.KeepLocal), Json))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized, "a peer arriving through the SSH forward must not resolve patches");
    }

    /// <summary>
    /// Verifies that a Mediator exposes no patch functionality.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Mediator_Has_No_Patch_Routes()
    {
        var patches = Substitute.For<IPatchService>();
        await using var host = await ServiceTestHost.StartAsync(NodeRole.Mediator, services => Replace(services, patches));

        (await PostEnvelopeAsync(host, "source-1", ServiceTestHost.LinkToken)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await PutContentAsync(host, Guid.NewGuid(), new string('a', 64), "x", "source-1", ServiceTestHost.LinkToken)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await PostCommitAsync(host, Guid.NewGuid(), "source-1", ServiceTestHost.LinkToken)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await host.CreateClient().PostAsync(ApiRoutes.SendPatch, null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await host.CreateClient().GetFromJsonAsync<NodeConfigurationDto>(ApiRoutes.Config, Json))!.Capabilities.ShowsSyncTab.Should().BeFalse();
        await patches.DidNotReceiveWithAnyArgs().OpenSessionAsync(default!, default!, default);
        await patches.DidNotReceiveWithAnyArgs().CommitAsync(default, default!, default);
    }

    /// <summary>
    /// Verifies that SignalR clients with the token receive published events and clients without it are refused.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task SignalR_Delivers_Events()
    {
        await using var host = await ServiceTestHost.StartAsync(NodeRole.Dest, services => Replace(services, Substitute.For<IPatchService>()));
        var received = new TaskCompletionSource<PatchEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var connection = CreateHubConnection(host, host.ApiToken);
        connection.On<PatchEvent>(NodeEventNames.PatchEvent, evt => received.TrySetResult(evt));
        await connection.StartAsync();

        var published = new PatchEvent(Guid.NewGuid(), PatchDirection.Incoming, PatchStatus.ConflictPending, 0, 2, "conflict", DateTimeOffset.UtcNow);
        await host.Services.GetRequiredService<IEventPublisher>().PublishPatchAsync(published);

        var evt = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        evt.PatchId.Should().Be(published.PatchId);
        evt.Status.Should().Be(PatchStatus.ConflictPending);

        await using var anonymous = CreateHubConnection(host, "wrong");
        var connect = () => anonymous.StartAsync();
        await connect.Should().ThrowAsync<HttpRequestException>();
    }

    /// <summary>
    /// Verifies links, role change, export and import through the API with the real data layer.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Links_Role_Export_And_Import()
    {
        await using var host = await ServiceTestHost.StartAsync(NodeRole.Source);
        var client = host.CreateClient();

        var link = new ChainLinkOptions { Name = "relay", PeerNodeId = "mediator-1", PeerRole = NodeRole.Mediator, Host = "203.0.113.4", SshUser = "jump", RemotePort = 22, Priority = 10 };
        (await client.PostAsJsonAsync(ApiRoutes.Links, link, Json)).StatusCode.Should().Be(HttpStatusCode.OK);
        var badLink = new ChainLinkOptions { Name = "bad", PeerNodeId = "x", Host = "-oProxyCommand=calc", SshUser = "u" };
        (await client.PostAsJsonAsync(ApiRoutes.Links, badLink, Json)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var exported = await client.GetFromJsonAsync<ExportedNodeConfiguration>($"{ApiRoutes.ConfigExport}?host=10.8.0.1&user=rcynq", Json);
        exported!.SigningPublicKey.Should().NotBeNullOrEmpty();
        exported.Role.Should().Be(NodeRole.Source);

        var destExport = exported with { NodeId = new NodeId("dest-1"), Name = "Dest", Role = NodeRole.Dest, Host = "10.8.0.2", GitRepoPath = "/srv/repo" };
        var import = await client.PostAsJsonAsync(ApiRoutes.ConfigImport, destExport, Json);
        import.StatusCode.Should().Be(HttpStatusCode.OK);
        (await import.Content.ReadFromJsonAsync<ImportResult>(Json))!.LinkCreated.Should().BeTrue();

        var links = (await client.GetFromJsonAsync<LinkDto[]>(ApiRoutes.Links, Json))!;
        links.Select(item => (item.Name, item.PeerRole)).Should().Equal(("relay", NodeRole.Mediator), ("dest-1", NodeRole.Dest));
        links[1].HasSigningKey.Should().BeTrue();
        links[1].RemotePort.Should().Be(exported.ListenPort);

        var role = await client.PutAsJsonAsync(ApiRoutes.ConfigRole, new SetRoleRequest(NodeRole.Dest, host.Root, null), Json);
        role.StatusCode.Should().Be(HttpStatusCode.OK);
        (await role.Content.ReadFromJsonAsync<RoleChangeResult>(Json))!.RestartRequired.Should().BeTrue();
        (await ConfigurationFileEditor.LoadAsync(host.ConfigPath)).Role.Should().Be(NodeRole.Dest);

        (await client.PutAsJsonAsync(ApiRoutes.ConfigRole, new SetRoleRequest(NodeRole.Source, null, null), Json)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Verifies that saving a link by hand does not erase what an import stored about its peer (the link form knows neither
    /// the signing key nor the repository path), and that a link can be removed and is gone afterwards.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Saving_A_Link_Keeps_The_Imported_Peer_And_A_Link_Can_Be_Removed()
    {
        await using var host = await ServiceTestHost.StartAsync(NodeRole.Source);
        var client = host.CreateClient();
        var exported = await client.GetFromJsonAsync<ExportedNodeConfiguration>($"{ApiRoutes.ConfigExport}?host=10.8.0.1&user=rcynq", Json);
        var destExport = exported! with { NodeId = new NodeId("dest-1"), Name = "Dest", Role = NodeRole.Dest, Host = "10.8.0.2", GitRepoPath = "/srv/repo" };
        (await client.PostAsJsonAsync(ApiRoutes.ConfigImport, destExport, Json)).StatusCode.Should().Be(HttpStatusCode.OK);

        var imported = (await client.GetFromJsonAsync<LinkDto[]>(ApiRoutes.Links, Json))!.Should().ContainSingle().Which;

        // The link is saved again by hand, with another address.
        var edited = new ChainLinkOptions { Id = imported.Id, Name = imported.Name, PeerNodeId = "dest-1", PeerRole = NodeRole.Dest, Host = "10.8.0.9", SshUser = "rcynq", RemotePort = 18443, Priority = 1000 };
        (await client.PostAsJsonAsync(ApiRoutes.Links, edited, Json)).StatusCode.Should().Be(HttpStatusCode.OK);

        var link = (await client.GetFromJsonAsync<LinkDto[]>(ApiRoutes.Links, Json))!.Should().ContainSingle().Which;
        link.Host.Should().Be("10.8.0.9");
        link.HasSigningKey.Should().BeTrue("without the key every patch of the peer would be refused");
        link.GitRepoPath.Should().Be("/srv/repo");

        (await client.DeleteAsync(ApiRoutes.LinkPath(link.Id))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetFromJsonAsync<LinkDto[]>(ApiRoutes.Links, Json))!.Should().BeEmpty();
        (await client.DeleteAsync(ApiRoutes.LinkPath(link.Id))).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Verifies that the node applies only a fix it proposed itself: a fix made up by the client is answered as not
    /// applied, and the same fix is applied once the node has proposed it.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Fix_Endpoint_Applies_Only_Fixes_The_Node_Proposed()
    {
        await using var host = await ServiceTestHost.StartAsync(NodeRole.Source);
        var client = host.CreateClient();
        var directory = Path.Combine(host.Root, "data", "created-by-fix");
        var fix = new DiagnosticFix(DiagnosticFixKind.CreateDirectory, "Create the directory.", new Dictionary<string, string> { ["path"] = directory });

        var refused = await client.PostAsJsonAsync(ApiRoutes.DiagnosticsFix, new ApplyFixRequest(fix, true), Json);

        refused.StatusCode.Should().Be(HttpStatusCode.OK);
        (await refused.Content.ReadFromJsonAsync<AutoFixResult>(Json))!.Applied.Should().BeFalse();
        Directory.Exists(directory).Should().BeFalse("the node did not propose this fix");

        host.Services.GetRequiredService<IssuedFixRegistry>().Remember(new[] { DiagnosticResult.Fail("Workdir", "missing", null, fix) });
        var applied = await client.PostAsJsonAsync(ApiRoutes.DiagnosticsFix, new ApplyFixRequest(fix, true), Json);

        (await applied.Content.ReadFromJsonAsync<AutoFixResult>(Json))!.Applied.Should().BeTrue();
        Directory.Exists(directory).Should().BeTrue();
    }

    /// <summary>
    /// Verifies that the data cleanup needs the local API token and removes the finished patches of the node.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Data_Cleanup_Requires_Token_And_Removes_Finished_Patches()
    {
        await using var host = await ServiceTestHost.StartAsync(NodeRole.Source);
        var patches = host.Services.GetRequiredService<RCynQ.Core.Abstractions.Repositories.IPatchRepository>();
        var hash = new string('a', 64);
        var finished = new PatchMetadata(
            Guid.NewGuid(), PatchDirection.Outgoing, new NodeId("source-1"), new NodeId("dest-1"), DateTimeOffset.UtcNow.AddDays(-2), hash, hash,
            PatchStatus.Failed, null, new[] { new FileChange(ChangeKind.Deleted, "old.txt", null, null, false, hash) });
        await patches.UpsertAsync(finished);

        (await host.CreateClient(withApiToken: false).PostAsync(ApiRoutes.DataCleanup, null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await patches.ExistsAsync(finished.Id)).Should().BeTrue();

        var response = await host.CreateClient().PostAsync(ApiRoutes.DataCleanup, null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<DataCleanupReport>(Json))!.Patches.Should().Be(1);
        (await patches.ExistsAsync(finished.Id)).Should().BeFalse();
    }

    /// <summary>
    /// Verifies that the shutdown endpoint needs the local API token and stops the application.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Shutdown_Requires_Token_And_Stops_Host()
    {
        await using var host = await ServiceTestHost.StartAsync(NodeRole.Mediator, services => Replace(services, Substitute.For<IPatchService>()));
        var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();

        (await host.CreateClient(withApiToken: false).PostAsync(ApiRoutes.ServiceShutdown, null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        lifetime.ApplicationStopping.IsCancellationRequested.Should().BeFalse();

        (await host.CreateClient().PostAsync(ApiRoutes.ServiceShutdown, null)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        lifetime.ApplicationStopping.IsCancellationRequested.Should().BeTrue();
    }

    /// <summary>
    /// Verifies the environment probe endpoint and the export with the detected SSH data.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Export_Probe_And_Fingerprint()
    {
        var probe = Substitute.For<INodeEnvironmentProbe>();
        probe.ProbeAsync(Arg.Any<CancellationToken>()).Returns(new NodeEnvironmentReport(
            "PC", "dev", 22, true, true,
            new[] { new NetworkAddressInfo("10.8.0.3", "awg0", NetworkAddressKind.Tunnel, true) },
            "10.8.0.3", null, null, null, Array.Empty<string>(), new[] { EnvironmentWarning.NoSshPublicKey }, DateTimeOffset.UtcNow));
        await using var host = await ServiceTestHost.StartAsync(NodeRole.Mediator, services =>
        {
            services.RemoveAll<INodeEnvironmentProbe>();
            services.AddSingleton(probe);
        });
        var client = host.CreateClient();

        (await host.CreateClient(withApiToken: false).GetAsync(ApiRoutes.ConfigExportProbe)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var report = await client.GetFromJsonAsync<NodeEnvironmentReport>(ApiRoutes.ConfigExportProbe, Json);
        report!.SuggestedHost.Should().Be("10.8.0.3");
        report.Warnings.Should().Equal(EnvironmentWarning.NoSshPublicKey);

        var fingerprint = Convert.ToBase64String(new byte[32]).TrimEnd('=');
        var exported = await client.GetFromJsonAsync<ExportedNodeConfiguration>($"{ApiRoutes.ConfigExport}?host=10.8.0.3&user=dev&hostKeyFingerprint={Uri.EscapeDataString(fingerprint)}", Json);
        exported!.HostKeyFingerprint.Should().Be("SHA256:" + fingerprint);
        (await client.GetAsync($"{ApiRoutes.ConfigExport}?host=10.8.0.3&hostKeyFingerprint=nope")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Replaces the patch service registration.
    /// </summary>
    /// <param name="services">The services.</param>
    /// <param name="patches">The replacement.</param>
    private static void Replace(IServiceCollection services, IPatchService patches)
    {
        services.RemoveAll<IPatchService>();
        services.AddSingleton(patches);
    }

    /// <summary>
    /// Posts a valid unsigned envelope, gzip-compressed, to <c>/patch/sessions</c>.
    /// </summary>
    /// <param name="host">The host.</param>
    /// <param name="sender">The X-Node-Id header value.</param>
    /// <param name="token">The X-Link-Token header value.</param>
    /// <returns>The response.</returns>
    private static async Task<HttpResponseMessage> PostEnvelopeAsync(ServiceTestHost host, string sender, string token)
    {
        var hash = new string('a', 64);
        var envelope = new PatchEnvelope(Guid.NewGuid(), new NodeId(sender), new NodeId("dest-1"), DateTimeOffset.UtcNow, hash, hash, Array.Empty<FileChange>());
        var body = GzipCodec.Compress(host.Services.GetRequiredService<PatchEnvelopeSerializer>().Serialize(envelope));
        using var request = new HttpRequestMessage(HttpMethod.Post, ApiRoutes.PatchSessions) { Content = new ByteArrayContent(body) };
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        request.Content.Headers.ContentEncoding.Add(GzipCodec.ContentEncoding);
        request.Headers.Add(ApiHeaders.NodeId, sender);
        request.Headers.Add(ApiHeaders.LinkToken, token);
        return await host.CreateClient(withApiToken: false).SendAsync(request);
    }

    /// <summary>
    /// Uploads gzip-compressed text as the content of a file of a session, streamed the way the sender does.
    /// </summary>
    /// <param name="host">The host.</param>
    /// <param name="patchId">The patch identifier.</param>
    /// <param name="sha256">The hash in the route.</param>
    /// <param name="text">The content.</param>
    /// <param name="sender">The X-Node-Id header value.</param>
    /// <param name="token">The X-Link-Token header value.</param>
    /// <returns>The response.</returns>
    private static async Task<HttpResponseMessage> PutContentAsync(ServiceTestHost host, Guid patchId, string sha256, string text, string sender, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, ApiRoutes.PatchSessionContentPath(patchId, sha256))
        {
            Content = new GzipStreamContent(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text)))
        };
        request.Headers.Add(ApiHeaders.NodeId, sender);
        request.Headers.Add(ApiHeaders.LinkToken, token);
        return await host.CreateClient(withApiToken: false).SendAsync(request);
    }

    /// <summary>
    /// Posts the commit of a session.
    /// </summary>
    /// <param name="host">The host.</param>
    /// <param name="patchId">The patch identifier.</param>
    /// <param name="sender">The X-Node-Id header value.</param>
    /// <param name="token">The X-Link-Token header value.</param>
    /// <returns>The response.</returns>
    private static async Task<HttpResponseMessage> PostCommitAsync(ServiceTestHost host, Guid patchId, string sender, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, ApiRoutes.PatchSessionCommitPath(patchId));
        request.Headers.Add(ApiHeaders.NodeId, sender);
        request.Headers.Add(ApiHeaders.LinkToken, token);
        return await host.CreateClient(withApiToken: false).SendAsync(request);
    }

    /// <summary>
    /// Creates a SignalR connection through the test server.
    /// </summary>
    /// <param name="host">The host.</param>
    /// <param name="token">The API token.</param>
    /// <returns>The connection.</returns>
    private static HubConnection CreateHubConnection(ServiceTestHost host, string token)
        => new HubConnectionBuilder()
            .WithUrl(new Uri(host.BaseAddress, $"{ApiRoutes.Hub.TrimStart('/')}?{ApiHeaders.AccessTokenQuery}={Uri.EscapeDataString(token)}"), options =>
            {
                options.HttpMessageHandlerFactory = _ => new SocketsHttpHandler { UseProxy = false };
                options.Transports = HttpTransportType.LongPolling;
            })
            .Build();
}
