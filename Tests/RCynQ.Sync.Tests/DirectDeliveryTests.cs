using System.Net;
using FluentAssertions;
using NSubstitute;
using RCynQ.Core.Abstractions.Ssh;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Ids;
using RCynQ.Git.Patches.Serialization;
using RCynQ.Ssh.Routing;
using RCynQ.Sync.Security;
using RCynQ.Sync.Tests.Fakes;
using RCynQ.Sync.Tests.Fixtures;
using RCynQ.Sync.Transfer;

namespace RCynQ.Sync.Tests;

/// <summary>
/// Tests of delivery to a peer that runs no SSH server and accepts connections on its own TLS port.
/// </summary>
public sealed class DirectDeliveryTests : IDisposable
{
    /// <summary>The shared Source and Dest fixture.</summary>
    private readonly SyncFixture _fixture = new();

    /// <summary>A pinned certificate fingerprint.</summary>
    private const string Fingerprint = "SHA256:" + "A1B2C3D4E5F60718293A4B5C6D7E8F90A1B2C3D4E5F60718293A4B5C6D7E8F90";

    /// <summary>The forward manager substitute.</summary>
    private readonly ILocalForwardManager _forwards = Substitute.For<ILocalForwardManager>();

    /// <summary>
    /// Verifies that a peer behind its SSH server keeps the old route: SSH into the peer, forward to its loopback.
    /// </summary>
    [Fact]
    public void Ssh_Peer_Is_Reached_Through_Its_Own_Ssh_Server()
    {
        var route = ChainRoute.Resolve(new[] { Link("relay", Peer("mediator-1", NodeRole.Mediator, "203.0.113.4"), 22, 10), Link("dest", Peer("dest-1", NodeRole.Dest, "10.8.0.2"), 18443, 1000) });

        route.UsesSsh.Should().BeTrue();
        route.UsesTls.Should().BeFalse();
        route.Target!.Host.Should().Be("10.8.0.2", "the SSH session ends at the peer itself");
        route.Jumps.Select(jump => jump.Host).Should().Equal("203.0.113.4");
        route.RemoteHost.Should().Be("127.0.0.1");
        route.RemotePort.Should().Be(18443);
    }

    /// <summary>
    /// Verifies that a peer without an SSH server is reached through the mediator, with TLS ending at the peer.
    /// </summary>
    [Fact]
    public void Direct_Peer_Behind_Mediator_Is_Reached_Through_The_Mediator()
    {
        var route = ChainRoute.Resolve(new[]
        {
            Link("relay", Peer("mediator-1", NodeRole.Mediator, "203.0.113.4"), 22, 10),
            Link("dest", Peer("dest-1", NodeRole.Dest, "192.168.10.20", EndpointMode.Direct), 18444, 1000)
        });

        route.UsesSsh.Should().BeTrue("the mediator still relays, it just stops there");
        route.Target!.Host.Should().Be("203.0.113.4", "the SSH session ends at the mediator");
        route.Jumps.Should().BeEmpty();
        route.RemoteHost.Should().Be("192.168.10.20", "the mediator forwards to the peer's own address");
        route.RemotePort.Should().Be(18444);
        route.TlsFingerprint.Should().Be(Fingerprint, "TLS is established with the peer, so the mediator sees only ciphertext");
    }

    /// <summary>
    /// Verifies that without a mediator no SSH is used at all.
    /// </summary>
    [Fact]
    public void Direct_Peer_Without_Mediator_Uses_No_Ssh()
    {
        var route = ChainRoute.Resolve(new[] { Link("dest", Peer("dest-1", NodeRole.Dest, "192.168.10.20", EndpointMode.Direct), 18444, 1000) });

        route.UsesSsh.Should().BeFalse();
        route.Target.Should().BeNull();
        route.RemoteHost.Should().Be("192.168.10.20");
        route.RemotePort.Should().Be(18444);
        route.UsesTls.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that a peer without an SSH server must publish both its fingerprint and its port.
    /// </summary>
    [Fact]
    public void Direct_Peer_Requires_Fingerprint_And_Port()
    {
        var withoutFingerprint = () => new PeerDescriptor(new NodeId("dest-1"), "d", NodeRole.Dest, "h", 22, "u", endpointMode: EndpointMode.Direct, endpointPort: 18444);
        var withoutPort = () => new PeerDescriptor(new NodeId("dest-1"), "d", NodeRole.Dest, "h", 22, "u", endpointMode: EndpointMode.Direct, tlsFingerprint: Fingerprint);

        withoutFingerprint.Should().Throw<ArgumentException>().WithParameterName("tlsFingerprint");
        withoutPort.Should().Throw<ArgumentException>().WithParameterName("endpointPort");
        Peer("dest-1", NodeRole.Dest, "h", EndpointMode.Direct).ReachablePort.Should().Be(18444, "neighbours connect to the TLS port");
        Peer("dest-1", NodeRole.Dest, "h").ReachablePort.Should().Be(22, "neighbours connect to the SSH server");
    }

    /// <summary>
    /// Verifies that a mediator without an SSH server cannot relay to a peer without one either.
    /// </summary>
    [Fact]
    public void Mediator_Without_Ssh_Cannot_Relay()
    {
        var resolve = () => ChainRoute.Resolve(new[]
        {
            Link("relay", Peer("mediator-1", NodeRole.Mediator, "203.0.113.4", EndpointMode.Direct), 22, 10),
            Link("dest", Peer("dest-1", NodeRole.Dest, "192.168.10.20", EndpointMode.Direct), 18444, 1000)
        });

        resolve.Should().Throw<ArgumentException>().WithMessage("*mediator-1*SSH server*");
    }

    /// <summary>
    /// Verifies that the sender posts over TLS to the peer and starts no forward when none is needed.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Sender_Posts_Over_Tls_Without_A_Forward()
    {
        var recorder = new RequestRecorder();
        var sender = Create(recorder);

        await sender.SendAsync(
            await _fixture.BuildSignedEnvelopeAsync(),
            _fixture.SourceContent,
            new[] { Link("dest", Peer("dest-1", NodeRole.Dest, "192.168.10.20", EndpointMode.Direct), 18444, 1000) });

        recorder.Uris.Should().OnlyContain(uri => uri.Scheme == "https" && uri.Host == "192.168.10.20" && uri.Port == 18444);
        recorder.Uris[0].AbsolutePath.Should().Be("/patch/sessions");
        recorder.PinnedFingerprint.Should().Be(Fingerprint, "the certificate of the peer is pinned");
        await _forwards.DidNotReceiveWithAnyArgs().StartAsync(default!, default!, default, default!, default);
    }

    /// <summary>
    /// Verifies that with a mediator the sender forwards to the peer's TLS port and posts through the forward.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Sender_Forwards_To_The_Tls_Port_Through_The_Mediator()
    {
        var recorder = new RequestRecorder();
        _forwards.StartAsync(Arg.Any<SshEndpoint>(), Arg.Any<IReadOnlyList<SshEndpoint>>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(19001);
        var sender = Create(recorder);

        await sender.SendAsync(await _fixture.BuildSignedEnvelopeAsync(), _fixture.SourceContent, new[]
        {
            Link("relay", Peer("mediator-1", NodeRole.Mediator, "203.0.113.4"), 22, 10),
            Link("dest", Peer("dest-1", NodeRole.Dest, "192.168.10.20", EndpointMode.Direct), 18444, 1000)
        });

        await _forwards.Received(1).StartAsync(
            Arg.Is<SshEndpoint>(target => target.Host == "203.0.113.4"),
            Arg.Is<IReadOnlyList<SshEndpoint>>(jumps => jumps.Count == 0),
            Arg.Any<int>(),
            "192.168.10.20",
            18444,
            Arg.Any<CancellationToken>());
        recorder.Uris.Should().OnlyContain(uri => uri.Scheme == "https" && uri.Host == "127.0.0.1" && uri.Port == 19001, "the forward is local, the certificate is still the peers");
        recorder.PinnedFingerprint.Should().Be(Fingerprint);
        await _forwards.Received(1).StopAsync(19001, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Creates a sender whose HTTP traffic is recorded.
    /// </summary>
    /// <param name="recorder">The recorder.</param>
    /// <returns>The sender.</returns>
    private HttpPatchSender Create(RequestRecorder recorder)
        => new(
            TestTexts.English, recorder,
            _forwards,
            _fixture.SourceSigner,
            new PatchEnvelopeSerializer(TestTexts.English, _fixture.Paths),
            new LinkTokenProvider(new SyncBridgeOptions { LinkToken = SyncFixture.Token }, new InMemorySecretStore()),
            _fixture.SourceNode,
            new HttpPatchSenderOptions { RetryDelay = TimeSpan.Zero },
            recorder.Pin);

    /// <summary>
    /// Creates a peer.
    /// </summary>
    /// <param name="id">The identifier.</param>
    /// <param name="role">The role.</param>
    /// <param name="host">The host.</param>
    /// <param name="mode">How the peer accepts connections.</param>
    /// <returns>The peer.</returns>
    private static PeerDescriptor Peer(string id, NodeRole role, string host, EndpointMode mode = EndpointMode.SshTunnel)
        => mode == EndpointMode.Direct
            ? new(new NodeId(id), id, role, host, 22, "rcynq", endpointMode: mode, tlsFingerprint: Fingerprint, endpointPort: 18444)
            : new(new NodeId(id), id, role, host, 22, "rcynq");

    /// <summary>
    /// Creates a chain link.
    /// </summary>
    /// <param name="name">The link name.</param>
    /// <param name="peer">The peer.</param>
    /// <param name="remotePort">The port on the peer side.</param>
    /// <param name="priority">The chain position.</param>
    /// <returns>The link.</returns>
    private static ChainLinkConfig Link(string name, PeerDescriptor peer, int remotePort, int priority)
        => new(Guid.NewGuid(), name, TransportKind.Direct, peer, 0, remotePort, priority);

    /// <inheritdoc />
    public void Dispose() => _fixture.Dispose();

    /// <summary>
    /// An HTTP handler that records the requests and answers like a peer that has all the content and accepts the patch.
    /// </summary>
    private sealed class RequestRecorder : HttpMessageHandler
    {
        /// <summary>Gets the URIs of the requests in the order they were made.</summary>
        public List<Uri> Uris { get; } = new();

        /// <summary>Gets the fingerprint the pinned handler was built for.</summary>
        public string? PinnedFingerprint { get; private set; }

        /// <summary>
        /// Records the fingerprint a pinned handler was requested for and reuses this handler.
        /// </summary>
        /// <param name="fingerprint">The fingerprint.</param>
        /// <returns>This handler.</returns>
        public HttpMessageHandler Pin(string fingerprint)
        {
            PinnedFingerprint = fingerprint;
            return this;
        }

        /// <inheritdoc />
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uris.Add(request.RequestUri!);
            if (!request.RequestUri!.AbsolutePath.EndsWith("/commit", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"patchId":"00000000-0000-0000-0000-000000000000","missingContent":[]}""")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"patchId":"00000000-0000-0000-0000-000000000000","status":"Applied","filesChanged":0,"conflicts":[],"message":"ok"}""")
            });
        }
    }
}
