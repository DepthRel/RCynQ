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
    /// <summary>The shared Source and Dest fixture; the node under test is Dest.</summary>
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
        await _fixture.RegisterSourcePeerAsync();
        var before = TimeProvider.System.GetUtcNow();

        var answer = await Service().AnswerAsync(new PatchReceiveContext("source-1", SyncFixture.Token));

        answer.NodeId.Should().Be("dest-1");
        answer.Role.Should().Be(NodeRole.Dest);
        answer.AnsweredAtUtc.Should().BeOnOrAfter(before);
        _contacts.LastHeard(_fixture.SourceNode.Id).Should().Be(answer.AnsweredAtUtc);
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
    [InlineData("source-1", "wrong-token", true, PatchRejectionReason.InvalidToken)]
    [InlineData("source-1", null, true, PatchRejectionReason.InvalidToken)]
    [InlineData("stranger-1", SyncFixture.Token, true, PatchRejectionReason.UnknownSender)]
    [InlineData("source-1", SyncFixture.Token, false, PatchRejectionReason.UnknownSender)]
    [InlineData(null, SyncFixture.Token, true, PatchRejectionReason.UnknownSender)]
    public async Task Others_Are_Refused(string? sender, string? token, bool knownWithKey, PatchRejectionReason expected)
    {
        if (knownWithKey)
        {
            await _fixture.RegisterSourcePeerAsync();
        }
        else
        {
            _fixture.Peers.GetAsync(_fixture.SourceNode.Id, Arg.Any<CancellationToken>())
                .Returns(new PeerDescriptor(_fixture.SourceNode.Id, "Source", NodeRole.Source, "source.local", 22, "rcynq"));
        }

        var answer = () => Service().AnswerAsync(new PatchReceiveContext(sender, token));

        (await answer.Should().ThrowAsync<PatchRejectedException>()).Which.Reason.Should().Be(expected);
        _contacts.LastHeard(_fixture.SourceNode.Id).Should().BeNull("a refused request is not a contact");
    }

    /// <summary>
    /// Verifies that a mediator, which exchanges no patches, answers no ping.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Mediator_Answers_No_Ping()
    {
        var mediator = new NodeConfig(new NodeId("mediator-1"), "Relay", NodeRole.Mediator, null, null);

        var answer = () => Service(mediator).AnswerAsync(new PatchReceiveContext("source-1", SyncFixture.Token));

        (await answer.Should().ThrowAsync<PatchRejectedException>()).Which.Reason.Should().Be(PatchRejectionReason.RoleNotAllowed);
    }

    /// <summary>
    /// Disposes the fixture.
    /// </summary>
    public void Dispose() => _fixture.Dispose();

    /// <summary>
    /// Creates the service of a node.
    /// </summary>
    /// <param name="node">The node; Dest when omitted.</param>
    /// <returns>The service.</returns>
    private PeerPingService Service(NodeConfig? node = null)
    {
        node ??= _fixture.DestNode;
        var admission = new PatchAdmission(
            TestTexts.English,
            node,
            _fixture.Peers,
            _fixture.DestSigner,
            new LinkTokenProvider(_fixture.DestOptions, new InMemorySecretStore()),
            _fixture.Patches);
        return new PeerPingService(admission, node, _contacts, TimeProvider.System);
    }
}
