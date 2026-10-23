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
    /// Verifies that <see cref="NodeRole"/> contains exactly Workstation, Relay and Origin.
    /// </summary>
    [Fact]
    public void NodeRole_Contains_Expected_Values()
    {
        Enum.GetValues<NodeRole>().Should().BeEquivalentTo(new[] { NodeRole.Workstation, NodeRole.Relay, NodeRole.Origin });
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
        Enum.GetNames<PatchStatus>().Should().Equal("Pending", "Applied", "Rejected", "ConflictPending", "ConflictResolved", "Failed", "Waiting");
        Enum.GetNames<LinkStatus>().Should().Equal("Unknown", "Starting", "Ready", "Degraded", "Stopped", "Failed");
    }

    /// <summary>
    /// Verifies case-insensitive parsing of role names.
    /// </summary>
    /// <param name="text">The input text.</param>
    /// <param name="expected">The expected role.</param>
    [Theory]
    [InlineData("workstation", NodeRole.Workstation)]
    [InlineData("RELAY", NodeRole.Relay)]
    [InlineData("Origin", NodeRole.Origin)]
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

    /// <summary>
    /// Verifies that only member names are parsed: a number or a list of names is not a name, even when the number is the
    /// value of a member.
    /// </summary>
    /// <param name="text">The input text.</param>
    [Theory]
    [InlineData("42")]
    [InlineData("1")]
    [InlineData("-1")]
    [InlineData("Workstation,Origin")]
    [InlineData("Workstation, Relay")]
    public void Validation_Rejects_Numbers_And_Lists(string text)
    {
        var parse = () => NodeRoleValidation.Parse(text);

        parse.Should().Throw<ArgumentException>();
        NodeRoleValidation.TryParse(text, true, out var role).Should().BeFalse();
        role.Should().Be(default(NodeRole));
        PatchStatusValidation.TryParse(text, true, out _).Should().BeFalse();
        TransportKindValidation.TryParse(text, true, out _).Should().BeFalse();
    }

    /// <summary>
    /// Verifies that the case of a name matters when it is asked to, and that surrounding white space does not.
    /// </summary>
    [Fact]
    public void Validation_Honours_Case_And_Trims()
    {
        NodeRoleValidation.TryParse("workstation", false, out _).Should().BeFalse();
        NodeRoleValidation.TryParse("Workstation", false, out var exact).Should().BeTrue();
        exact.Should().Be(NodeRole.Workstation);
        NodeRoleValidation.TryParse("  origin ", true, out var trimmed).Should().BeTrue();
        trimmed.Should().Be(NodeRole.Origin);
    }
}
