using FluentAssertions;
using NSubstitute;
using RCynQ.Core.Enums;
using RCynQ.UI.Enums;
using RCynQ.UI.ViewModels;
using RCynQ.UI.ViewModels.Navigation;

namespace RCynQ.UI.Tests.Navigation;

/// <summary>
/// Tests of <see cref="RoleAdapter"/>.
/// </summary>
public sealed class RoleAdapterTests
{
    /// <summary>
    /// Verifies which tabs each role shows.
    /// </summary>
    /// <param name="role">The role.</param>
    /// <param name="tab">The tab.</param>
    /// <param name="expected">Whether the tab is visible.</param>
    [Theory]
    [InlineData(NodeRole.Relay, TabKey.Sync, false)]
    [InlineData(NodeRole.Relay, TabKey.Branches, false)]
    [InlineData(NodeRole.Relay, TabKey.Tunnel, true)]
    [InlineData(NodeRole.Workstation, TabKey.Sync, true)]
    [InlineData(NodeRole.Workstation, TabKey.Branches, true)]
    [InlineData(NodeRole.Origin, TabKey.Sync, true)]
    [InlineData(NodeRole.Origin, TabKey.Diagnostics, true)]
    public void Visibility_Follows_Role(NodeRole role, TabKey tab, bool expected)
        => new RoleAdapter().IsAvailable(tab, role).Should().Be(expected);

    /// <summary>
    /// Verifies that a hidden selected tab moves the selection to the first visible tab.
    /// </summary>
    [Fact]
    public void Apply_Moves_Selection_Away_From_Hidden_Tab()
    {
        var page = Substitute.For<PageViewModelBase>(TestTexts.Russian);
        var overview = new NavigationItemViewModel(TestTexts.Russian, TabKey.Overview, "Icon.Home", page);
        var sync = new NavigationItemViewModel(TestTexts.Russian, TabKey.Sync, "Icon.Sync", page) { IsSelected = true };

        var selected = new RoleAdapter().Apply(new[] { overview, sync }, NodeRole.Relay);

        sync.IsAvailable.Should().BeFalse();
        selected.Should().BeSameAs(overview);
    }
}
