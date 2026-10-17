using System.Text.Json.Nodes;
using FluentAssertions;
using NSubstitute;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Abstractions.Security;
using RCynQ.Core.Api;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Ids;
using RCynQ.Sync.Configuration;

namespace RCynQ.Sync.Tests;

/// <summary>
/// Tests of the host key fingerprint handling in <see cref="NodeConfigurationTransfer"/>.
/// </summary>
public sealed class NodeConfigurationTransferTests
{
    /// <summary>A valid SHA-256 fingerprint body (43 Base64 characters).</summary>
    private static readonly string _fingerprintBody = Convert.ToBase64String(new byte[32]).TrimEnd('=');

    /// <summary>The peer repository substitute.</summary>
    private readonly IPeerRepository _peers = Substitute.For<IPeerRepository>();

    /// <summary>The link repository substitute.</summary>
    private readonly IChainLinkRepository _links = Substitute.For<IChainLinkRepository>();

    /// <summary>The signature service substitute.</summary>
    private readonly ISignatureService _signatures = Substitute.For<ISignatureService>();

    /// <summary>
    /// Initializes a new instance of the <see cref="NodeConfigurationTransferTests"/> class.
    /// </summary>
    public NodeConfigurationTransferTests()
    {
        _links.GetAllAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<ChainLinkConfig>());
        _links.UpsertAsync(Arg.Any<ChainLinkConfig>(), Arg.Any<CancellationToken>()).Returns(call => call.Arg<ChainLinkConfig>());
        _peers.UpsertAsync(Arg.Any<PeerDescriptor>(), Arg.Any<CancellationToken>()).Returns(call => call.Arg<PeerDescriptor>());
    }

    /// <summary>
    /// Verifies that the exported fingerprint is normalized and survives serialization.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Export_Includes_Normalized_Fingerprint()
    {
        var exported = await Create(NodeRole.Relay).ExportAsync(new[] { "203.0.113.4" }, "jump", 22, null, _fingerprintBody);

        exported.HostKeyFingerprint.Should().Be("SHA256:" + _fingerprintBody);
        NodeConfigurationTransfer.Parse(NodeConfigurationTransfer.Serialize(exported)).HostKeyFingerprint.Should().Be("SHA256:" + _fingerprintBody);
    }

    /// <summary>
    /// Verifies that an invalid fingerprint is refused on export and on parse.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Invalid_Fingerprint_Is_Refused()
    {
        var transfer = Create(NodeRole.Relay);

        var export = () => transfer.ExportAsync(new[] { "203.0.113.4" }, "jump", 22, null, "SHA256:short");
        await export.Should().ThrowAsync<ArgumentException>();

        var valid = await transfer.ExportAsync(new[] { "203.0.113.4" }, "jump");
        var parse = () => NodeConfigurationTransfer.Parse(NodeConfigurationTransfer.Serialize(valid with { HostKeyFingerprint = "garbage\"with quotes" }));
        parse.Should().Throw<FormatException>();
    }

    /// <summary>
    /// Verifies that an imported fingerprint is pinned on the peer and its link.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Import_Pins_Host_Key_Fingerprint()
    {
        var imported = new ExportedNodeConfiguration(
            ExportedNodeConfiguration.CURRENT_FORMAT_VERSION,
            new NodeId("relay-1"),
            "Relay",
            NodeRole.Relay,
            null,
            "203.0.113.4",
            "jump",
            22,
            18443,
            null,
            DateTimeOffset.UtcNow,
            _fingerprintBody);

        var result = await Create(NodeRole.Workstation).ImportAsync(imported);

        result.LinkCreated.Should().BeTrue();
        await _peers.Received(1).UpsertAsync(Arg.Is<PeerDescriptor>(peer => peer.HostKeyFingerprint == "SHA256:" + _fingerprintBody), Arg.Any<CancellationToken>());
        await _links.Received(1).UpsertAsync(Arg.Is<ChainLinkConfig>(link => link.Peer.HostKeyFingerprint == "SHA256:" + _fingerprintBody), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies that the SSH public key of an imported peer is stored for the neighbour access.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Import_Stores_Ssh_Public_Key()
    {
        const string KEY = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIOMqqnkVzrm0SdG6UOoqKLsabgH5C9okWi0dh2l9GKJl rcynq:workstation-1";
        var imported = new ExportedNodeConfiguration(
            ExportedNodeConfiguration.CURRENT_FORMAT_VERSION, new NodeId("workstation-1"), "Workstation", NodeRole.Workstation, null,
            "10.8.1.3", "dev", 22, 18443, "  " + KEY + "  ", DateTimeOffset.UtcNow);

        await Create(NodeRole.Origin).ImportAsync(imported);

        await _peers.Received(1).UpsertAsync(Arg.Is<PeerDescriptor>(peer => peer.SshPublicKey == KEY), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies that importing a newer export of the counterpart moves its link to the port the export names: an Origin that
    /// switched from the SSH tunnel to its own TLS port is reached there, not on the old loopback port.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Reimport_Of_The_Counterpart_Updates_The_Port_Of_Its_Link()
    {
        var originId = new NodeId("origin-1");
        var stored = new PeerDescriptor(originId, "Origin", NodeRole.Origin, "10.8.0.2", 22, "rcynq");
        var link = new ChainLinkConfig(Guid.NewGuid(), "origin-1", TransportKind.Direct, stored, 0, 18443, NodeConfigurationTransfer.ENDPOINT_PRIORITY);
        _peers.GetAsync(originId, Arg.Any<CancellationToken>()).Returns(stored);
        _links.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new[] { link });
        var tls = "SHA256:" + new string('A', 64);
        var imported = new ExportedNodeConfiguration(
            ExportedNodeConfiguration.CURRENT_FORMAT_VERSION, originId, "Origin", NodeRole.Origin, null,
            "10.8.0.2", "rcynq", 22, 18443, null, DateTimeOffset.UtcNow,
            EndpointMode: EndpointMode.Direct, EndpointPort: 18444, TlsFingerprint: tls);

        var result = await Create(NodeRole.Workstation).ImportAsync(imported);

        result.LinkCreated.Should().BeFalse();
        await _links.Received(1).UpsertAsync(
            Arg.Is<ChainLinkConfig>(saved => saved.Id == link.Id && saved.RemotePort == 18444 && saved.Peer.EndpointMode == EndpointMode.Direct),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies that importing a relay at a new address as its replacement leaves the port of its link alone: an export
    /// says nothing about it.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Reimport_Of_A_Relay_Keeps_The_Port_Of_Its_Link()
    {
        var relayId = new NodeId("relay-1");
        var stored = new PeerDescriptor(relayId, "Relay", NodeRole.Relay, "203.0.113.4", 22, "jump");
        var link = new ChainLinkConfig(Guid.NewGuid(), "relay", TransportKind.Direct, stored, 0, 2222, 10);
        _peers.GetAsync(relayId, Arg.Any<CancellationToken>()).Returns(stored);
        _links.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new[] { link });
        var imported = new ExportedNodeConfiguration(
            ExportedNodeConfiguration.CURRENT_FORMAT_VERSION, relayId, "Relay", NodeRole.Relay, null,
            "203.0.113.9", "jump", 22, 18443, null, DateTimeOffset.UtcNow);

        await Create(NodeRole.Workstation).ImportAsync(imported, ImportDecision.Replace, null);

        await _links.Received(1).UpsertAsync(
            Arg.Is<ChainLinkConfig>(saved => saved.RemotePort == 2222 && saved.Peer.Host == "203.0.113.9"),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies that a known node at a new address is not imported without a decision: the node does not guess whether
    /// the address is another way to it or its new address.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Known_Node_At_A_New_Address_Waits_For_A_Decision()
    {
        var link = StoredRelayLink("203.0.113.4");

        var result = await Create(NodeRole.Workstation).ImportAsync(RelayExport("relay-1", "10.9.0.4"));

        result.NeedsDecision.Should().BeTrue();
        result.Decision!.SameNode.Should().BeTrue();
        result.Decision.Options.Should().Equal(ImportDecision.AlternativeRoute, ImportDecision.Replace);
        result.Decision.Candidates.Should().ContainSingle().Which.LinkId.Should().Be(link.Id);
        await _links.DidNotReceive().UpsertAsync(Arg.Any<ChainLinkConfig>(), Arg.Any<CancellationToken>());
        await _peers.DidNotReceive().UpsertAsync(Arg.Any<PeerDescriptor>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies that an alternative route keeps the address in use and adds the new one next to it.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Alternative_Route_Is_Added_To_The_Link_Of_The_Node()
    {
        StoredRelayLink("203.0.113.4");

        var result = await Create(NodeRole.Workstation).ImportAsync(RelayExport("relay-1", "10.9.0.4"), ImportDecision.AlternativeRoute, null);

        result.NeedsDecision.Should().BeFalse();
        await _links.Received(1).UpsertAsync(
            Arg.Is<ChainLinkConfig>(saved =>
                saved.Peer.Host == "203.0.113.4" &&
                saved.Routes.Count == 2 &&
                saved.Routes.Any(route => route.Ssh.Host == "203.0.113.4") &&
                saved.Routes.Any(route => route.Ssh.Host == "10.9.0.4") &&
                saved.ActiveRoute!.Ssh.Host == "203.0.113.4"),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies that importing a node again at an address that is one of its routes asks nothing and keeps the route in
    /// use.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Known_Route_Is_Refreshed_Without_A_Decision()
    {
        var link = StoredRelayLink("203.0.113.4");
        var routed = link.WithRoutes(new[]
        {
            new LinkRoute(Guid.NewGuid(), "udp", link.Peer.Ssh),
            new LinkRoute(Guid.NewGuid(), "tcp", new SshEndpoint("jump", "10.9.0.4", 22))
        });
        _links.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new[] { routed });

        var result = await Create(NodeRole.Workstation).ImportAsync(RelayExport("relay-1", "10.9.0.4"));

        result.NeedsDecision.Should().BeFalse();
        await _links.Received(1).UpsertAsync(
            Arg.Is<ChainLinkConfig>(saved => saved.Peer.Host == "203.0.113.4" && saved.Routes.Count == 2),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies that another Relay is not silently added to the chain when one is linked already, and that it can be
    /// added as one more hop.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Another_Relay_Waits_For_A_Decision_And_Can_Become_A_Hop()
    {
        StoredRelayLink("203.0.113.4");
        var transfer = Create(NodeRole.Workstation);

        var asked = await transfer.ImportAsync(RelayExport("relay-2", "198.51.100.7"));

        asked.Decision!.SameNode.Should().BeFalse();
        asked.Decision.Options.Should().Equal(ImportDecision.AddHop, ImportDecision.Replace);
        await _links.DidNotReceive().UpsertAsync(Arg.Any<ChainLinkConfig>(), Arg.Any<CancellationToken>());

        var added = await transfer.ImportAsync(RelayExport("relay-2", "198.51.100.7"), ImportDecision.AddHop, null);

        added.LinkCreated.Should().BeTrue();
        await _links.Received(1).UpsertAsync(
            Arg.Is<ChainLinkConfig>(saved => saved.Peer.Id == new NodeId("relay-2") && saved.Priority == 20),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies that a node that replaces another takes its link and that the replaced node is forgotten, so its keys
    /// lose their access.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Replacing_A_Node_Keeps_The_Link_And_Forgets_The_Replaced_Node()
    {
        var link = StoredRelayLink("203.0.113.4");

        var result = await Create(NodeRole.Workstation).ImportAsync(RelayExport("relay-2", "198.51.100.7"), ImportDecision.Replace, link.Id);

        result.LinkCreated.Should().BeFalse();
        result.LinkId.Should().Be(link.Id);
        await _links.Received(1).UpsertAsync(
            Arg.Is<ChainLinkConfig>(saved =>
                saved.Id == link.Id && saved.Priority == link.Priority && saved.RemotePort == link.RemotePort &&
                saved.Peer.Id == new NodeId("relay-2") && saved.Routes.Count == 0),
            Arg.Any<CancellationToken>());
        await _peers.Received(1).DeleteUnlinkedAsync(new NodeId("relay-1"), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies that a second counterpart endpoint can only replace the known one: a chain ends at one node.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Another_Counterpart_Can_Only_Replace_The_Known_One()
    {
        var origin = new PeerDescriptor(new NodeId("origin-1"), "Origin", NodeRole.Origin, "10.8.0.2", 22, "rcynq");
        _links.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new[]
        {
            new ChainLinkConfig(Guid.NewGuid(), "origin", TransportKind.Direct, origin, 0, 18443, NodeConfigurationTransfer.ENDPOINT_PRIORITY)
        });
        var other = new ExportedNodeConfiguration(
            ExportedNodeConfiguration.CURRENT_FORMAT_VERSION, new NodeId("origin-2"), "Origin", NodeRole.Origin, null,
            "10.8.0.9", "rcynq", 22, 18443, null, DateTimeOffset.UtcNow);
        var transfer = Create(NodeRole.Workstation);

        (await transfer.ImportAsync(other)).Decision!.Options.Should().Equal(ImportDecision.Replace);

        var hop = () => transfer.ImportAsync(other, ImportDecision.AddHop, null);
        await hop.Should().ThrowAsync<FormatException>();
        await _links.DidNotReceive().UpsertAsync(Arg.Any<ChainLinkConfig>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies that the first node of a role is imported without a question, as it always was.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task First_Node_Of_A_Role_Is_Imported_Silently()
    {
        var result = await Create(NodeRole.Workstation).ImportAsync(RelayExport("relay-1", "203.0.113.4"));

        result.NeedsDecision.Should().BeFalse();
        result.LinkCreated.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that a link written by hand keeps the routes an import gave it and the route the node chose.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Saving_A_Configured_Link_Keeps_Its_Routes_And_The_Route_In_Use()
    {
        var relayId = new NodeId("relay-1");
        var tcp = new SshEndpoint("jump", "10.9.0.4", 22);
        var peer = new PeerDescriptor(relayId, "Relay", NodeRole.Relay, tcp.Host, tcp.Port, tcp.User);
        var id = SyncBridgeOptionsValidator.StableId("relay");
        var known = new ChainLinkConfig(id, "relay", TransportKind.Direct, peer, 0, 22, 10, routes: new[]
        {
            new LinkRoute(Guid.NewGuid(), "udp", new SshEndpoint("jump", "203.0.113.4", 22)),
            new LinkRoute(Guid.NewGuid(), "tcp", tcp)
        });
        _peers.GetAsync(relayId, Arg.Any<CancellationToken>()).Returns(peer);
        _links.GetAsync(id, Arg.Any<CancellationToken>()).Returns(known);

        var saved = await new ConfiguredLinkStore(_links, _peers).SaveAsync(new ChainLinkOptions
        {
            Name = "relay", PeerNodeId = "relay-1", PeerRole = NodeRole.Relay, Host = "203.0.113.4", SshUser = "jump", RemotePort = 22, Priority = 10
        });

        saved.Routes.Should().HaveCount(2, "the configuration file names one address and must not erase the others");
        saved.Peer.Host.Should().Be("10.9.0.4", "the node chose this route while running");
    }

    /// <summary>
    /// Verifies that an export lists every address in the given order, names the first one as the host for nodes of
    /// 0.1.0, carries the protocol version and survives serialization.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Export_Lists_Every_Address_With_The_First_As_Host()
    {
        var exported = await Create(NodeRole.Relay).ExportAsync(new[] { "203.0.113.4", "10.9.0.4" }, "jump");

        exported.Host.Should().Be("203.0.113.4");
        exported.Addresses.Should().Equal("203.0.113.4", "10.9.0.4");
        exported.ProtocolVersion.Should().Be(ExportedNodeConfiguration.CURRENT_PROTOCOL_VERSION);
        exported.FormatVersion.Should().Be(1, "a node of 0.1.0 refuses any other format version");

        var parsed = NodeConfigurationTransfer.Parse(NodeConfigurationTransfer.Serialize(exported));
        parsed.Addresses.Should().Equal("203.0.113.4", "10.9.0.4");
        parsed.ReachableAt().Should().Equal("203.0.113.4", "10.9.0.4");
    }

    /// <summary>
    /// Verifies that an export refuses an address that is not a host name or an IP address.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Export_Refuses_An_Invalid_Address()
    {
        var export = () => Create(NodeRole.Relay).ExportAsync(new[] { "203.0.113.4", "bad host;rm" }, "jump");

        await export.Should().ThrowAsync<ArgumentException>();
    }

    /// <summary>
    /// Verifies that a file of 0.1.0, without the address list, is read with its host as the only address.
    /// </summary>
    [Fact]
    public void Card_Of_An_Earlier_Version_Is_Read_With_Its_Host()
    {
        // A file written by 0.1.0 has no such fields at all, not even as null.
        var old = JsonNode.Parse(NodeConfigurationTransfer.Serialize(RelayExport("relay-1", "203.0.113.4")))!.AsObject();
        old.Remove("addresses");
        old.Remove("protocolVersion");

        var parsed = NodeConfigurationTransfer.Parse(old.ToJsonString());

        parsed.Addresses.Should().BeNull();
        parsed.ProtocolVersion.Should().BeNull();
        parsed.ReachableAt().Should().Equal("203.0.113.4");
    }

    /// <summary>
    /// Verifies that a file with an unsafe address or with too many addresses is refused.
    /// </summary>
    [Fact]
    public void Card_With_Bad_Addresses_Is_Refused()
    {
        var card = RelayExport("relay-1", "203.0.113.4");

        var unsafeAddress = () => NodeConfigurationTransfer.Parse(NodeConfigurationTransfer.Serialize(card with { Addresses = new[] { "203.0.113.4", "x\" command=\"sh" } }));
        var tooMany = () => NodeConfigurationTransfer.Parse(NodeConfigurationTransfer.Serialize(card with
        {
            Addresses = Enumerable.Range(1, ExportedNodeConfiguration.MAX_ADDRESSES + 1).Select(i => $"10.0.0.{i}").ToArray()
        }));

        unsafeAddress.Should().Throw<FormatException>();
        tooMany.Should().Throw<FormatException>();
    }

    /// <summary>
    /// Verifies that every address of a card becomes a route of the new link, in the order of the card, and the link
    /// starts at the first one.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Import_Turns_Every_Address_Into_A_Route()
    {
        var card = RelayExport("relay-1", "203.0.113.4") with { Addresses = new[] { "203.0.113.4", "10.9.0.4" } };

        var result = await Create(NodeRole.Workstation).ImportAsync(card);

        result.LinkCreated.Should().BeTrue();
        await _links.Received(1).UpsertAsync(
            Arg.Is<ChainLinkConfig>(saved =>
                saved.Peer.Host == "203.0.113.4" &&
                saved.Routes.Select(route => route.Ssh.Host).SequenceEqual(new[] { "203.0.113.4", "10.9.0.4" }) &&
                saved.Routes[0].Priority < saved.Routes[1].Priority),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies that an address a card repeats becomes one route, and a card that names one address twice gives a link
    /// without routes.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Import_Makes_One_Route_Per_Repeated_Address()
    {
        var repeated = RelayExport("relay-1", "203.0.113.4") with { Addresses = new[] { "203.0.113.4", "10.9.0.4", "203.0.113.4" } };
        var single = RelayExport("relay-2", "198.51.100.7") with { Addresses = new[] { "198.51.100.7", "198.51.100.7" } };

        await Create(NodeRole.Workstation).ImportAsync(repeated);
        await Create(NodeRole.Workstation).ImportAsync(single);

        await _links.Received(1).UpsertAsync(
            Arg.Is<ChainLinkConfig>(saved => saved.Peer.Id.Value == "relay-1" && saved.Routes.Select(route => route.Ssh.Host).SequenceEqual(new[] { "203.0.113.4", "10.9.0.4" })),
            Arg.Any<CancellationToken>());
        await _links.Received(1).UpsertAsync(
            Arg.Is<ChainLinkConfig>(saved => saved.Peer.Id.Value == "relay-2" && saved.Routes.Count == 0),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies that a new card of a known node replaces the routes of its link without a question, keeps a pinned route
    /// whose address is still in the card and keeps using it.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task New_Card_Of_A_Known_Node_Replaces_Its_Routes()
    {
        var link = StoredRelayLink("203.0.113.4");
        var tcp = new LinkRoute(Guid.NewGuid(), "tcp", new SshEndpoint("jump", "10.9.0.4", 22));
        var routed = link.WithRoutes(new[] { new LinkRoute(Guid.NewGuid(), "udp", link.Peer.Ssh), tcp }).WithPinnedRoute(tcp.Id);
        _links.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new[] { routed });
        var card = RelayExport("relay-1", "198.51.100.7") with { Addresses = new[] { "198.51.100.7", "10.9.0.4" } };

        var result = await Create(NodeRole.Workstation).ImportAsync(card);

        result.NeedsDecision.Should().BeFalse();
        await _links.Received(1).UpsertAsync(
            Arg.Is<ChainLinkConfig>(saved =>
                saved.Routes.Select(route => route.Ssh.Host).SequenceEqual(new[] { "198.51.100.7", "10.9.0.4" }) &&
                saved.PinnedRouteId == tcp.Id &&
                saved.Peer.Host == "10.9.0.4"),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies that a new card of a known node whose address in use is gone moves the link to the first address of
    /// the card, and that a card with one address leaves a link without routes.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task New_Card_Without_The_Address_In_Use_Moves_The_Link_To_Its_First_Address()
    {
        StoredRelayLink("203.0.113.4");
        var card = RelayExport("relay-1", "198.51.100.7") with { Addresses = new[] { "198.51.100.7" } };

        var result = await Create(NodeRole.Workstation).ImportAsync(card);

        result.NeedsDecision.Should().BeFalse();
        await _links.Received(1).UpsertAsync(
            Arg.Is<ChainLinkConfig>(saved => saved.Routes.Count == 0 && saved.Peer.Host == "198.51.100.7" && saved.PinnedRouteId == null),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Stores a link of a Workstation to Relay <c>relay-1</c>.
    /// </summary>
    /// <param name="host">The address of the Relay.</param>
    /// <returns>The link.</returns>
    private ChainLinkConfig StoredRelayLink(string host)
    {
        var relayId = new NodeId("relay-1");
        var stored = new PeerDescriptor(relayId, "Relay", NodeRole.Relay, host, 22, "jump");
        var link = new ChainLinkConfig(Guid.NewGuid(), "relay", TransportKind.Direct, stored, 0, 2222, 10);
        _peers.GetAsync(relayId, Arg.Any<CancellationToken>()).Returns(stored);
        _links.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new[] { link });
        return link;
    }

    /// <summary>
    /// Builds the export of a Relay.
    /// </summary>
    /// <param name="id">The node identifier.</param>
    /// <param name="host">The exported address.</param>
    /// <returns>The export.</returns>
    private static ExportedNodeConfiguration RelayExport(string id, string host)
        => new(
            ExportedNodeConfiguration.CURRENT_FORMAT_VERSION, new NodeId(id), "Relay", NodeRole.Relay, null,
            host, "jump", 22, 18443, null, DateTimeOffset.UtcNow);

    /// <summary>
    /// Verifies that saving a hand-written link keeps what an import stored about its peer: the SSH key, the signing key
    /// and the endpoint mode are not part of such a link and must not be erased by it.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Saving_A_Configured_Link_Keeps_The_Imported_Details_Of_Its_Peer()
    {
        const string SSH_KEY = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIOMqqnkVzrm0SdG6UOoqKLsabgH5C9okWi0dh2l9GKJl rcynq:origin-1";
        var originId = new NodeId("origin-1");
        var signingKey = Convert.ToBase64String(new byte[] { 1, 2, 3 });
        var tls = "SHA256:" + new string('B', 64);
        _peers.GetAsync(originId, Arg.Any<CancellationToken>()).Returns(new PeerDescriptor(
            originId, "Origin", NodeRole.Origin, "10.8.0.2", 22, "rcynq", "SHA256:" + _fingerprintBody, signingKey, SSH_KEY, EndpointMode.Direct, tls, 18444));
        var store = new ConfiguredLinkStore(_links, _peers);

        var saved = await store.SaveAsync(new ChainLinkOptions
        {
            Name = "origin", PeerNodeId = "origin-1", PeerRole = NodeRole.Origin, Host = "10.8.0.7", SshUser = "sync", RemotePort = 18444, Priority = 1000
        });

        saved.Peer.Host.Should().Be("10.8.0.7", "the link states the address");
        saved.Peer.SshUser.Should().Be("sync");
        saved.Peer.SshPublicKey.Should().Be(SSH_KEY);
        saved.Peer.SigningPublicKey.Should().Be(signingKey);
        saved.Peer.HostKeyFingerprint.Should().Be("SHA256:" + _fingerprintBody);

        saved.Peer.EndpointMode.Should().Be(EndpointMode.Direct);
        saved.Peer.TlsFingerprint.Should().Be(tls);
        saved.Peer.EndpointPort.Should().Be(18444);
    }

    /// <summary>
    /// Verifies that a value the link states wins over the stored one, and that a new peer is saved as written.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Saving_A_Configured_Link_Lets_Its_Own_Values_Win()
    {
        var originId = new NodeId("origin-1");
        _peers.GetAsync(originId, Arg.Any<CancellationToken>()).Returns(new PeerDescriptor(originId, "Origin", NodeRole.Origin, "10.8.0.2", 22, "rcynq"));
        var store = new ConfiguredLinkStore(_links, _peers);

        var known = await store.SaveAsync(new ChainLinkOptions { Name = "origin", PeerNodeId = "origin-1", PeerRole = NodeRole.Origin, Host = "10.8.0.2", SshUser = "sync" });
        var fresh = await store.SaveAsync(new ChainLinkOptions { Name = "relay", PeerNodeId = "relay-1", PeerRole = NodeRole.Relay, Host = "203.0.113.4", SshUser = "jump", RemotePort = 22 });

        known.Peer.SshUser.Should().Be("sync");
        fresh.Peer.SshPublicKey.Should().BeNull();
        fresh.Peer.EndpointMode.Should().Be(EndpointMode.SshTunnel);
    }

    /// <summary>
    /// Creates the transfer for a local node.
    /// </summary>
    /// <param name="role">The local role.</param>
    /// <returns>The transfer.</returns>
    private NodeConfigurationTransfer Create(NodeRole role)
        => new(
            TestTexts.English,
            new NodeConfig(new NodeId("node-1"), "Node", role, role == NodeRole.Origin ? "/srv/repo" : null, role == NodeRole.Workstation ? "/work" : null),
            new SyncBridgeOptions { NodeId = "node-1", Role = role },
            _signatures,
            _peers,
            _links,
            TimeProvider.System);
}
