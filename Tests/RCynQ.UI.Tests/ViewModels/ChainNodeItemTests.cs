using FluentAssertions;
using RCynQ.Core.Api;
using RCynQ.Core.Enums;
using RCynQ.Core.Ids;
using RCynQ.UI.ViewModels.Items;

namespace RCynQ.UI.Tests.ViewModels;

/// <summary>
/// Tests of <see cref="ChainNodeItemViewModel"/>: a node is one line, and a node reachable at several addresses unfolds
/// to list them.
/// </summary>
public sealed class ChainNodeItemTests
{
    /// <summary>
    /// Verifies that a node with several addresses starts folded and unfolds and folds again on a click of its line.
    /// </summary>
    [Fact]
    public void Node_With_Routes_Starts_Folded_And_Toggles()
    {
        var node = Node();
        node.ApplyRoutes(Routes(node.LinkId));

        node.HasRoutes.Should().BeTrue();
        node.AreRoutesShown.Should().BeFalse("a node is one line until it is unfolded");

        node.ToggleCommand.Execute(null);
        node.AreRoutesShown.Should().BeTrue();
        node.Address.Should().Be("10.0.0.2", "the line shows the address in use");

        node.ToggleCommand.Execute(null);
        node.AreRoutesShown.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that an unfolded node without routes shows nothing under its line, and shows its routes once they come.
    /// </summary>
    [Fact]
    public void Unfolded_Node_Shows_Routes_Only_When_It_Has_Them()
    {
        var node = Node();
        var changed = new List<string?>();
        node.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        node.IsExpanded = true;
        node.AreRoutesShown.Should().BeFalse();

        node.ApplyRoutes(Routes(node.LinkId));
        node.AreRoutesShown.Should().BeTrue();
        changed.Should().Contain(nameof(ChainNodeItemViewModel.AreRoutesShown));
    }

    /// <summary>
    /// Creates the row of a Relay.
    /// </summary>
    /// <returns>The row.</returns>
    private static ChainNodeItemViewModel Node() => new(
        TestTexts.English,
        new LinkDto(
            Guid.NewGuid(), "relay", TransportKind.Direct, Array.Empty<TransportKind>(), new Dictionary<string, string>(),
            new NodeId("relay-1"), "Relay", NodeRole.Relay, "10.0.0.1", 22, "rcynq", null, false, 0, 22, 10));

    /// <summary>
    /// Builds two routes of a link, the second one in use.
    /// </summary>
    /// <param name="linkId">The link.</param>
    /// <returns>The routes.</returns>
    private static LinkRoutesDto Routes(Guid linkId) => new(linkId, null, new[]
    {
        new LinkRouteDto(Guid.NewGuid(), "lan", "10.0.0.1", 22, "rcynq", 0, false, RouteReachability.Unreachable, null, null),
        new LinkRouteDto(Guid.NewGuid(), "vpn", "10.0.0.2", 22, "rcynq", 1, true, RouteReachability.Reachable, 12, null)
    });
}
