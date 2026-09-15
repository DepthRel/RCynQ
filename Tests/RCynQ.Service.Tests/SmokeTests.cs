using FluentAssertions;
using RCynQ.Core.Api;
using RCynQ.Core.Enums;
using RCynQ.Service.Endpoints;
using Xunit;

namespace RCynQ.Service.Tests;

/// <summary>
/// Smoke tests for role capabilities and rejection status mapping.
/// </summary>
public class SmokeTests
{
    /// <summary>
    /// Verifies that a Mediator has no patch or Git capabilities.
    /// </summary>
    [Fact]
    public void Role_Capabilities_Match_Roles()
    {
        RoleCapabilities.For(NodeRole.Mediator).Should().Be(new RoleCapabilities(false, false, false, false, true));
        RoleCapabilities.For(NodeRole.Source).FetchesGit.Should().BeTrue();
        RoleCapabilities.For(NodeRole.Dest).ServesGit.Should().BeTrue();
        RoleCapabilities.For(NodeRole.Mediator).ShowsSyncTab.Should().BeFalse();
    }

    /// <summary>
    /// Verifies the status codes of rejection reasons.
    /// </summary>
    [Fact]
    public void Rejection_Reasons_Map_To_Status_Codes()
    {
        PatchRejectionStatusCodes.For(PatchRejectionReason.InvalidToken).Should().Be(401);
        PatchRejectionStatusCodes.For(PatchRejectionReason.InvalidSignature).Should().Be(403);
        PatchRejectionStatusCodes.For(PatchRejectionReason.Replay).Should().Be(409);
        PatchRejectionStatusCodes.For(PatchRejectionReason.Malformed).Should().Be(400);
    }

    /// <summary>
    /// Verifies parsing of the parent process argument.
    /// </summary>
    /// <param name="args">The arguments separated by spaces.</param>
    /// <param name="expected">The expected identifier, or 0 when none is accepted.</param>
    [Theory]
    [InlineData("--config a.json --parent-pid 1234", 1234)]
    [InlineData("--parent-pid", 0)]
    [InlineData("--parent-pid -5", 0)]
    [InlineData("--parent-pid abc", 0)]
    [InlineData("--config a.json", 0)]
    public void Parent_Process_Argument_Is_Parsed(string args, int expected)
    {
        var found = Hosting.ServiceHostBuilder.TryGetParentProcessId(args.Split(' '), out var processId);

        found.Should().Be(expected > 0);
        if (found)
        {
            processId.Should().Be(expected);
        }
    }
}
