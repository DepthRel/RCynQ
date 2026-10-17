using System.Buffers.Text;
using System.IO.Compression;
using FluentAssertions;
using RCynQ.Core.Api;
using RCynQ.Core.Enums;
using RCynQ.Core.Ids;
using RCynQ.Sync.Enrollment;

namespace RCynQ.Sync.Tests;

/// <summary>
/// Tests of <see cref="InvitationCodec"/>: the text survives a mail program, and anything damaged or out of place is
/// refused before use.
/// </summary>
public sealed class InvitationCodecTests
{
    /// <summary>
    /// Verifies that an invitation comes back as it was, also with the line breaks and spaces a mail program adds.
    /// </summary>
    [Fact]
    public void Invitation_Survives_Line_Breaks()
    {
        var invitation = Sample();

        var text = InvitationCodec.Encode(invitation);
        var wrapped = string.Join("\r\n  ", Enumerable.Range(0, (text.Length + 59) / 60).Select(i => text.Substring(i * 60, Math.Min(60, text.Length - (i * 60)))));
        var read = InvitationCodec.Decode(" " + wrapped + "\n");

        text.Should().StartWith(Invitation.PREFIX);
        read.Inviter.NodeId.Should().Be(invitation.Inviter.NodeId);
        read.Inviter.Addresses.Should().Equal("203.0.113.4", "10.9.0.4");
        read.Ticket.Should().Be(invitation.Ticket);
        read.LinkToken.Should().Be(invitation.LinkToken);
        read.Path.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that damaged or foreign text is refused with a message.
    /// </summary>
    /// <param name="text">The text.</param>
    [Theory]
    [InlineData("")]
    [InlineData("hello")]
    [InlineData("rcynq-invite:")]
    [InlineData("rcynq-invite:!!!!")]
    [InlineData("rcynq-invite:aGVsbG8")]
    public void Damaged_Text_Is_Refused(string text)
    {
        var decode = () => InvitationCodec.Decode(text);

        decode.Should().Throw<FormatException>().Which.Message.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// Verifies that an invitation with a ticket for a Relay, a key that is not an OpenSSH key, a short link token or a
    /// path through a node that is not a Relay is refused.
    /// </summary>
    [Fact]
    public void Invitation_Out_Of_Place_Is_Refused()
    {
        var invitation = Sample();

        var variants = new[]
        {
            invitation with { Ticket = invitation.Ticket with { Role = NodeRole.Relay } },
            invitation with { Ticket = invitation.Ticket with { EnrollmentKey = "ssh-ed25519 AAAA" } },
            invitation with { Ticket = invitation.Ticket with { Secret = "with spaces and \"quotes\"" } },
            invitation with { LinkToken = "short" },
            invitation with { Path = new[] { invitation.Inviter with { Role = NodeRole.Origin } } },
            invitation with { Version = 2 }
        };

        foreach (var variant in variants)
        {
            var decode = () => InvitationCodec.Decode(InvitationCodec.Encode(variant));
            decode.Should().Throw<FormatException>();
        }
    }

    /// <summary>
    /// Verifies that data that inflates beyond the limit is refused while it inflates.
    /// </summary>
    [Fact]
    public void Inflating_Too_Far_Is_Refused()
    {
        using var buffer = new MemoryStream();
        using (var deflate = new DeflateStream(buffer, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            deflate.Write(new byte[2 * 1024 * 1024]);
        }

        var decode = () => InvitationCodec.Decode(Invitation.PREFIX + Base64Url.EncodeToString(buffer.ToArray()));

        decode.Should().Throw<FormatException>();
    }

    /// <summary>
    /// Builds an invitation of an Origin for a Workstation.
    /// </summary>
    /// <returns>The invitation.</returns>
    private static Invitation Sample()
    {
        var card = new ExportedNodeConfiguration(
            ExportedNodeConfiguration.CURRENT_FORMAT_VERSION, new NodeId("relay-1"), "Relay", NodeRole.Relay, null,
            "203.0.113.4", "rcynq", 22, 18443, null, DateTimeOffset.UnixEpoch,
            Addresses: new[] { "203.0.113.4", "10.9.0.4" }, ProtocolVersion: ExportedNodeConfiguration.CURRENT_PROTOCOL_VERSION);
        var ticket = new InvitationTicket(Guid.NewGuid(), "c2VjcmV0LXNlY3JldC1zZWNyZXQ", NodeRole.Origin, DateTimeOffset.UnixEpoch.AddDays(1),
            "-----BEGIN OPENSSH PRIVATE KEY-----\nb3BlbnNzaA==\n-----END OPENSSH PRIVATE KEY-----\n");
        return new Invitation(Invitation.CURRENT_VERSION, ExportedNodeConfiguration.CURRENT_PROTOCOL_VERSION, card, Array.Empty<ExportedNodeConfiguration>(), ticket);
    }
}
