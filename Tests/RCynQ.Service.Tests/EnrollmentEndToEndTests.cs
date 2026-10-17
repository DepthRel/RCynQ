using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Api;
using RCynQ.Core.Enums;
using RCynQ.Core.Ids;
using RCynQ.Service.Tests.Support;
using RCynQ.Ssh.Keys.AuthorizedKeys;
using RCynQ.Sync.Configuration.Probing;
using RCynQ.Sync.Enrollment;
using RCynQ.Sync.Signing;

namespace RCynQ.Service.Tests;

/// <summary>
/// Creates an invitation on a running Origin service and enrolls a Workstation with it over the public route, the way the
/// forced command of the one-time key passes it on.
/// </summary>
public sealed class EnrollmentEndToEndTests : IAsyncLifetime
{
    /// <summary>The SSH key of the Origin.</summary>
    private const string ORIGIN_KEY = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIPBzgtLuBSnvmhyPyE/aR3fT7G5Ynge9cP0KZC9vUVUb rcynq:origin-1";

    /// <summary>The SSH key of the Workstation.</summary>
    private const string WORKSTATION_KEY = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIOMqqnkVzrm0SdG6UOoqKLsabgH5C9okWi0dh2l9GKJl rcynq:workstation-1";

    /// <summary>The temporary folder of the authorized_keys file.</summary>
    private readonly string _folder = Directory.CreateTempSubdirectory("rcynq-enroll-e2e-").FullName;

    /// <summary>The running Origin.</summary>
    private ServiceTestHost _host = null!;

    /// <summary>Gets the authorized_keys file of the Origin.</summary>
    private string AuthorizedKeysPath => Path.Combine(_folder, "authorized_keys");

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        var probe = Substitute.For<INodeEnvironmentProbe>();
        probe.ProbeAsync(Arg.Any<CancellationToken>()).Returns(new NodeEnvironmentReport(
            "ORIGIN", "rcynq", 22, true, true,
            new[] { new NetworkAddressInfo("10.20.0.5", "eth0", NetworkAddressKind.Private, true) },
            "10.20.0.5", ORIGIN_KEY, null, null, Array.Empty<string>(), Array.Empty<EnvironmentWarning>(), DateTimeOffset.UtcNow));
        var command = Substitute.For<IEnrollmentCommandSource>();
        command.GetCommand().Returns(new[] { "/opt/rcynq/cli/rcynq", "enroll", "--config", "/etc/rcynq/syncbridge.json" });

        _host = await ServiceTestHost.StartAsync(
            NodeRole.Origin,
            services =>
            {
                services.Replace(ServiceDescriptor.Singleton(probe));
                services.Replace(ServiceDescriptor.Singleton(command));
            },
            new Dictionary<string, string?> { ["SyncBridge:Ssh:AuthorizedKeysPath"] = AuthorizedKeysPath });
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        await _host.DisposeAsync();
        Directory.Delete(_folder, recursive: true);
    }

    /// <summary>
    /// Verifies the whole exchange on the inviting side: the invitation, its one-time key in authorized_keys, the
    /// enrollment without any token, the single use and the list of invitations.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Workstation_Enrolls_Once_With_The_Invitation_Of_The_Origin()
    {
        using var api = _host.CreateClient();
        using var anonymous = _host.CreateClient(withApiToken: false);

        var created = await api.PostAsJsonAsync(ApiRoutes.CONFIG_INVITATIONS, new CreateInvitationRequest(NodeRole.Workstation, 24));
        created.StatusCode.Should().Be(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        var invitation = InvitationCodec.Decode((await created.Content.ReadFromJsonAsync<CreatedInvitation>())!.Text);
        invitation.Inviter.NodeId.Should().Be(new NodeId("origin-1"));
        invitation.LinkToken.Should().Be(ServiceTestHost.LINK_TOKEN);
        invitation.Ticket.EnrollmentKey.Should().StartWith("-----BEGIN OPENSSH PRIVATE KEY-----");
        (await File.ReadAllLinesAsync(AuthorizedKeysPath)).Should().ContainSingle(line => line.Contains(AuthorizedKeysManager.ENROLLMENT_COMMENT_PREFIX, StringComparison.Ordinal));

        var request = new EnrollmentRequest(invitation.Ticket.Id, invitation.Ticket.Secret, await WorkstationCardAsync());
        var first = await (await anonymous.PostAsJsonAsync(ApiRoutes.PEER_ENROLL, request)).Content.ReadFromJsonAsync<EnrollmentResponse>();
        var second = await (await anonymous.PostAsJsonAsync(ApiRoutes.PEER_ENROLL, request)).Content.ReadFromJsonAsync<EnrollmentResponse>();

        first!.Accepted.Should().BeTrue(first.Message);
        second!.Accepted.Should().BeFalse("an invitation is used once");
        (await _host.Services.GetRequiredService<IPeerRepository>().GetAsync(new NodeId("workstation-1"))).Should().NotBeNull();
        var lines = await File.ReadAllLinesAsync(AuthorizedKeysPath);
        lines.Should().NotContain(line => line.Contains(AuthorizedKeysManager.ENROLLMENT_COMMENT_PREFIX, StringComparison.Ordinal));
        lines.Should().Contain(line => line.EndsWith("rcynq:workstation-1", StringComparison.Ordinal));

        var list = await api.GetFromJsonAsync<InvitationDto[]>(ApiRoutes.CONFIG_INVITATIONS);
        list.Should().ContainSingle().Which.UsedBy.Should().Be("workstation-1");
        (await anonymous.GetAsync(ApiRoutes.CONFIG_INVITATIONS)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the list needs the local API token");
    }

    /// <summary>
    /// Verifies that an enrollment with a made-up ticket is answered with a refusal, in the language it asks for.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Made_Up_Ticket_Is_Refused_In_The_Language_Of_The_Request()
    {
        using var anonymous = _host.CreateClient(withApiToken: false);
        using var message = new HttpRequestMessage(HttpMethod.Post, ApiRoutes.PEER_ENROLL)
        {
            Content = JsonContent.Create(new EnrollmentRequest(Guid.NewGuid(), "bWFkZS11cA", await WorkstationCardAsync()))
        };
        message.Headers.AcceptLanguage.ParseAdd("ru");

        var response = await (await anonymous.SendAsync(message)).Content.ReadFromJsonAsync<EnrollmentResponse>();

        response!.Accepted.Should().BeFalse();
        response.Message.Should().Be(TestTexts.Russian.Get("Enrollment.TicketRefused"));
    }

    /// <summary>
    /// Builds the card of a Workstation.
    /// </summary>
    /// <returns>The card.</returns>
    private static async Task<ExportedNodeConfiguration> WorkstationCardAsync()
    {
        using var signer = new EcdsaSignatureService(TestTexts.English, new InMemorySecretStore());
        return new ExportedNodeConfiguration(
            ExportedNodeConfiguration.CURRENT_FORMAT_VERSION, new NodeId("workstation-1"), "Workstation", NodeRole.Workstation,
            Convert.ToBase64String(await signer.ExportPublicKeyAsync()), "10.30.0.7", "rcynq", 22, 18443, WORKSTATION_KEY, DateTimeOffset.UtcNow,
            Addresses: new[] { "10.30.0.7" }, ProtocolVersion: ExportedNodeConfiguration.CURRENT_PROTOCOL_VERSION);
    }
}
