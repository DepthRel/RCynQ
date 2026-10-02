using FluentAssertions;
using NSubstitute;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Ids;
using RCynQ.Core.Patches;
using RCynQ.Sync.Reachability;
using RCynQ.Sync.Security;
using RCynQ.Sync.Tests.Fakes;
using RCynQ.Sync.Tests.Fixtures;
using RCynQ.Sync.Transfer;

namespace RCynQ.Sync.Tests;

/// <summary>
/// Tests of the answer to a ping: a node says it is ready only to the counterpart endpoint it would accept a patch from.
/// </summary>
public sealed class PeerPingServiceTests : IDisposable
{
    /// <summary>The shared Workstation and Origin fixture; the node under test is Origin.</summary>
    private readonly SyncFixture _fixture = new();

    /// <summary>The contacts the node has had from its peers.</summary>
    private readonly PeerContactRegistry _contacts = new();

    /// <summary>
    /// Verifies that the counterpart endpoint with the right token gets the identity of the node, and that its contact
    /// is remembered.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Known_Peer_With_The_Token_Is_Answered_And_Remembered()
    {
        await _fixture.RegisterWorkstationPeerAsync();
        var before = TimeProvider.System.GetUtcNow();

        var answer = await Service().AnswerAsync(new PatchReceiveContext("workstation-1", SyncFixture.TOKEN));

        answer.NodeId.Should().Be("origin-1");
        answer.Role.Should().Be(NodeRole.Origin);
        answer.AnsweredAtUtc.Should().BeOnOrAfter(before);
        _contacts.LastHeard(_fixture.WorkstationNode.Id).Should().Be(answer.AnsweredAtUtc);
    }

    /// <summary>
    /// Verifies who is refused, with the reason a patch of the same sender would be refused for: a wrong token, a node
    /// this one does not know, a node known without its signing key, and a node that names nobody.
    /// </summary>
    /// <param name="sender">The identifier the request names.</param>
    /// <param name="token">The link token of the request.</param>
    /// <param name="knownWithKey">Whether the sender is a known peer with a signing key.</param>
    /// <param name="expected">The reason of the refusal.</param>
    /// <returns>A task representing the test.</returns>
    [Theory]
    [InlineData("workstation-1", "wrong-token", true, PatchRejectionReason.InvalidToken)]
    [InlineData("workstation-1", null, true, PatchRejectionReason.InvalidToken)]
    [InlineData("stranger-1", SyncFixture.TOKEN, true, PatchRejectionReason.UnknownSender)]
    [InlineData("workstation-1", SyncFixture.TOKEN, false, PatchRejectionReason.UnknownSender)]
    [InlineData(null, SyncFixture.TOKEN, true, PatchRejectionReason.UnknownSender)]
    public async Task Others_Are_Refused(string? sender, string? token, bool knownWithKey, PatchRejectionReason expected)
    {
        if (knownWithKey)
        {
            await _fixture.RegisterWorkstationPeerAsync();
        }
        else
        {
            _fixture.Peers.GetAsync(_fixture.WorkstationNode.Id, Arg.Any<CancellationToken>())
                .Returns(new PeerDescriptor(_fixture.WorkstationNode.Id, "Workstation", NodeRole.Workstation, "workstation.local", 22, "rcynq"));
        }

        var answer = () => Service().AnswerAsync(new PatchReceiveContext(sender, token));

        (await answer.Should().ThrowAsync<PatchRejectedException>()).Which.Reason.Should().Be(expected);
        _contacts.LastHeard(_fixture.WorkstationNode.Id).Should().BeNull("a refused request is not a contact");
    }

    /// <summary>
    /// Verifies that a relay, which exchanges no patches, answers no ping.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Relay_Answers_No_Ping()
    {
        var relay = new NodeConfig(new NodeId("relay-1"), "Relay", NodeRole.Relay, null, null);

        var answer = () => Service(relay).AnswerAsync(new PatchReceiveContext("workstation-1", SyncFixture.TOKEN));

        (await answer.Should().ThrowAsync<PatchRejectedException>()).Which.Reason.Should().Be(PatchRejectionReason.RoleNotAllowed);
    }

    /// <summary>
    /// Disposes the fixture.
    /// </summary>
    public void Dispose() => _fixture.Dispose();

    /// <summary>
    /// Creates the service of a node.
    /// </summary>
    /// <param name="node">The node; Origin when omitted.</param>
    /// <returns>The service.</returns>
    private PeerPingService Service(NodeConfig? node = null)
    {
        node ??= _fixture.OriginNode;
        var admission = new PatchAdmission(
            TestTexts.English,
            node,
            _fixture.Peers,
            _fixture.OriginSigner,
            new LinkTokenProvider(_fixture.OriginOptions, new InMemorySecretStore()),
            _fixture.Patches);
        return new PeerPingService(admission, node, _contacts, TimeProvider.System);
    }
}
