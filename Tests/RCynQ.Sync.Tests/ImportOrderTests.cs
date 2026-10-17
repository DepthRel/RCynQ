using FluentAssertions;
using NSubstitute;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Abstractions.Security;
using RCynQ.Core.Api;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Ids;
using RCynQ.Sync.Configuration;
using RCynQ.Sync.Configuration.Access;
using RCynQ.Sync.Tests.Fakes;

namespace RCynQ.Sync.Tests;

/// <summary>
/// Verifies that the cards of the neighbours may be imported in any order: every order gives the same chain and the
/// same access lines.
/// </summary>
public sealed class ImportOrderTests
{
    /// <summary>The SSH public key every card carries; the access lines only need a valid one.</summary>
    private const string SSH_KEY = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIOMqqnkVzrm0SdG6UOoqKLsabgH5C9okWi0dh2l9GKJl";

    /// <summary>
    /// Verifies the orders of every role: a Workstation and an Origin import a Relay and their counterpart, a Relay
    /// imports both endpoints.
    /// </summary>
    /// <param name="role">The role of the importing node.</param>
    /// <returns>A task representing the test.</returns>
    [Theory]
    [InlineData(NodeRole.Workstation)]
    [InlineData(NodeRole.Origin)]
    [InlineData(NodeRole.Relay)]
    public async Task Every_Order_Gives_The_Same_Chain_And_Access(NodeRole role)
    {
        var cards = CardsFor(role);

        var forward = await ImportAsync(role, cards);
        var backward = await ImportAsync(role, cards.Reverse().ToArray());

        backward.Chain.Should().Equal(forward.Chain);
        backward.Access.Should().Equal(forward.Access);
        forward.Chain.Should().HaveCount(2);
    }

    /// <summary>
    /// Imports the cards in the given order into an empty node and describes the result.
    /// </summary>
    /// <param name="role">The role of the importing node.</param>
    /// <param name="cards">The cards in the order of import.</param>
    /// <returns>The chain, one line per link, and the access lines.</returns>
    private static async Task<(IReadOnlyList<string> Chain, IReadOnlyList<string> Access)> ImportAsync(NodeRole role, IReadOnlyList<ExportedNodeConfiguration> cards)
    {
        var store = new InMemoryChainStore();
        var node = new NodeConfig(new NodeId("node-1"), "Node", role, role == NodeRole.Origin ? "/srv/repo" : null, role == NodeRole.Workstation ? "/work" : null);
        var transfer = new NodeConfigurationTransfer(
            TestTexts.English, node, new SyncBridgeOptions { NodeId = "node-1", Role = role }, Substitute.For<ISignatureService>(), store, store, TimeProvider.System);

        foreach (var card in cards)
        {
            var result = await transfer.ImportAsync(card);
            result.NeedsDecision.Should().BeFalse();
        }

        var links = await store.GetAllAsync();
        var chain = links
            .OrderBy(link => link.Peer.Id.Value, StringComparer.Ordinal)
            // A Relay forwards to its neighbours and never walks a chain, so the order of its links means nothing.
            .Select(link => $"{link.Peer.Id.Value} {link.Peer.Role} {link.Peer.Host}:{link.RemotePort} " +
                $"[{string.Join(",", link.Routes.Select(route => route.Ssh.Host))}] {(role == NodeRole.Relay ? "-" : link.Priority.ToString(System.Globalization.CultureInfo.InvariantCulture))}")
            .ToArray();
        var plan = PeerAccessPlanner.Plan(node, 18443, await ((IPeerRepository)store).GetAllAsync(), links);
        return (chain, plan.Entries.Select(entry => entry.Line).Order(StringComparer.Ordinal).ToArray());
    }

    /// <summary>
    /// Builds the cards a node of the role imports.
    /// </summary>
    /// <param name="role">The role of the importing node.</param>
    /// <returns>The cards.</returns>
    private static ExportedNodeConfiguration[] CardsFor(NodeRole role) => role switch
    {
        NodeRole.Workstation => new[] { Card("relay-1", NodeRole.Relay, "203.0.113.4", "10.9.0.4"), Card("origin-1", NodeRole.Origin, "10.20.0.5", "192.168.1.5") },
        NodeRole.Origin => new[] { Card("relay-1", NodeRole.Relay, "203.0.113.4", "10.9.0.4"), Card("workstation-1", NodeRole.Workstation, "10.30.0.7") },
        _ => new[] { Card("workstation-1", NodeRole.Workstation, "10.30.0.7"), Card("origin-1", NodeRole.Origin, "10.20.0.5", "192.168.1.5") }
    };

    /// <summary>
    /// Builds a card with the given addresses.
    /// </summary>
    /// <param name="id">The node identifier.</param>
    /// <param name="role">The role of the node.</param>
    /// <param name="addresses">The addresses, the preferred one first.</param>
    /// <returns>The card.</returns>
    private static ExportedNodeConfiguration Card(string id, NodeRole role, params string[] addresses)
        => new(
            ExportedNodeConfiguration.CURRENT_FORMAT_VERSION, new NodeId(id), id, role, null,
            addresses[0], "rcynq", 22, 18443, SSH_KEY, DateTimeOffset.UnixEpoch,
            Addresses: addresses, ProtocolVersion: ExportedNodeConfiguration.CURRENT_PROTOCOL_VERSION);
}
