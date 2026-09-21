using FluentAssertions;
using RCynQ.Core.Enums;
using RCynQ.Core.Validation;

namespace RCynQ.Core.Tests;

/// <summary>
/// Tests for the domain enums and their validation helpers.
/// </summary>
public class NodeRoleTests
{
    /// <summary>
    /// Verifies that <see cref="NodeRole"/> contains exactly Source, Mediator and Dest.
    /// </summary>
    [Fact]
    public void NodeRole_Contains_Expected_Values()
    {
        Enum.GetValues<NodeRole>().Should().BeEquivalentTo(new[] { NodeRole.Source, NodeRole.Mediator, NodeRole.Dest });
    }

    /// <summary>
    /// Verifies the enum members required by the architecture document.
    /// </summary>
    [Fact]
    public void Enums_Match_Architecture()
    {
        Enum.GetNames<TransportKind>().Should().Equal("Direct", "AmneziaWG", "WireGuard", "OpenConnect", "Tailscale");
        Enum.GetNames<ChangeKind>().Should().Equal("Added", "Modified", "Deleted", "Renamed");
        Enum.GetNames<ConflictPolicy>().Should().Equal("KeepLocal", "ApplyIncomingOverLocal", "UseGit");
        Enum.GetNames<PatchStatus>().Should().Equal("Pending", "Applied", "Rejected", "ConflictPending", "ConflictResolved", "Failed");
        Enum.GetNames<LinkStatus>().Should().Equal("Unknown", "Starting", "Ready", "Degraded", "Stopped", "Failed");
    }

    /// <summary>
    /// Verifies case-insensitive parsing of role names.
    /// </summary>
    /// <param name="text">The input text.</param>
    /// <param name="expected">The expected role.</param>
    [Theory]
    [InlineData("source", NodeRole.Source)]
    [InlineData("MEDIATOR", NodeRole.Mediator)]
    [InlineData("Dest", NodeRole.Dest)]
    public void NodeRoleValidation_Parses_Names(string text, NodeRole expected)
    {
        NodeRoleValidation.Parse(text).Should().Be(expected);
    }

    /// <summary>
    /// Verifies that invalid names and undefined values are rejected.
    /// </summary>
    [Fact]
    public void Validation_Rejects_Invalid_Values()
    {
        var parseBlank = () => NodeRoleValidation.Parse(" ");
        var parseUnknown = () => TransportKindValidation.Parse("carrier-pigeon");

        parseBlank.Should().Throw<ArgumentException>();
        parseUnknown.Should().Throw<ArgumentException>();
        PatchStatusValidation.IsDefined((PatchStatus)42).Should().BeFalse();
        TransportKindValidation.TryParse("amneziawg", true, out var kind).Should().BeTrue();
        kind.Should().Be(TransportKind.AmneziaWG);
    }
}
