using FluentAssertions;
using NSubstitute;
using RCynQ.Core.Api;
using RCynQ.Core.Enums;
using RCynQ.Core.State;
using RCynQ.UI.Services.Api;
using RCynQ.UI.Services.Dialogs;
using RCynQ.UI.ViewModels;
using RCynQ.UI.ViewModels.Items;
using RCynQ.UI.ViewModels.Pages;

namespace RCynQ.UI.Tests.ViewModels;

/// <summary>
/// Tests of <see cref="BranchStatusFilterViewModel"/>: the two axes of the filter of the branch switcher, and how the
/// switcher follows them.
/// </summary>
public sealed class BranchStatusFilterTests
{
    /// <summary>
    /// Verifies which branches pass each set of check marks: the choices of one axis add up, the two axes narrow each
    /// other, and an axis with nothing checked lets everything through.
    /// </summary>
    /// <param name="pulled">Whether "pulled" is checked.</param>
    /// <param name="notPulled">Whether "not pulled" is checked.</param>
    /// <param name="onOrigin">Whether "on the Origin" is checked.</param>
    /// <param name="localOnly">Whether "only local" is checked.</param>
    /// <param name="expected">The branches that pass, by name.</param>
    [Theory]
    [InlineData(false, false, false, false, "pulled,not-pulled,local")]
    [InlineData(true, false, false, false, "pulled,local")]
    [InlineData(false, true, false, false, "not-pulled")]
    [InlineData(true, true, false, false, "pulled,not-pulled,local")]
    [InlineData(false, false, true, false, "pulled,not-pulled")]
    [InlineData(false, false, false, true, "local")]
    [InlineData(true, false, true, false, "pulled")]
    [InlineData(true, false, false, true, "local")]
    [InlineData(false, true, false, true, "")]
    public void Branches_Pass_By_Both_Axes(bool pulled, bool notPulled, bool onOrigin, bool localOnly, string expected)
    {
        var filter = new BranchStatusFilterViewModel
        {
            ShowPulled = pulled,
            ShowNotPulled = notPulled,
            ShowOnOrigin = onOrigin,
            ShowLocalOnly = localOnly
        };

        var passed = Branches().Where(filter.Matches).Select(branch => branch.Name);

        passed.Should().Equal(expected.Split(',', StringSplitOptions.RemoveEmptyEntries));
        filter.IsActive.Should().Be(pulled || notPulled || onOrigin || localOnly);
    }

    /// <summary>
    /// Verifies that Apply keeps the check marks and closes the panel, and Reset clears them, says so once and closes it.
    /// </summary>
    [Fact]
    public void Apply_Keeps_And_Reset_Clears_The_Check_Marks_And_Both_Close_The_Panel()
    {
        var filter = new BranchStatusFilterViewModel { ShowOnOrigin = true, ShowLocalOnly = true, IsOpen = true };
        var changes = 0;
        filter.Changed += (_, _) => changes++;

        filter.ApplyCommand.Execute(null);

        filter.IsOpen.Should().BeFalse();
        filter.IsActive.Should().BeTrue();
        changes.Should().Be(0);

        filter.IsOpen = true;
        filter.ResetCommand.Execute(null);

        filter.IsOpen.Should().BeFalse();
        filter.IsActive.Should().BeFalse();
        changes.Should().Be(1, "the list is filtered once for the whole reset");
    }

    /// <summary>
    /// Verifies that a check mark narrows the switcher at once, together with the search by name.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task A_Check_Mark_Narrows_The_Switcher_At_Once()
    {
        var api = Substitute.For<IServiceApiClient>();
        api.GetBranchAsync(Arg.Any<CancellationToken>()).Returns(new GitBranchInfo(true, "pulled", null, 0, 0));
        api.GetSwitchableBranchesAsync(Arg.Any<CancellationToken>())
            .Returns(new GitSwitchBranchesResponse("pulled", "pulled", Branches().Select(branch => new GitSwitchableBranch(branch.Name, branch.Status, 0, 0)).ToArray()));
        var page = new BranchViewModel(TestTexts.Russian, api, Substitute.For<IDialogService>());
        page.Configure(RoleCapabilities.For(NodeRole.Workstation));
        await page.EnsureLoadedAsync();

        page.StatusFilter.ShowOnOrigin = true;

        page.FilteredBranches.Select(branch => branch.Name).Should().Equal("pulled", "not-pulled");

        page.BranchFilter = "NOT";

        page.FilteredBranches.Select(branch => branch.Name).Should().Equal("not-pulled");

        page.StatusFilter.ShowLocalOnly = false;
        page.StatusFilter.ShowPulled = true;

        page.HasNoFilteredBranches.Should().BeTrue();

        page.StatusFilter.ResetCommand.Execute(null);

        page.FilteredBranches.Select(branch => branch.Name).Should().Equal("not-pulled");
    }

    /// <summary>
    /// Builds one branch of each state.
    /// </summary>
    /// <returns>The branches: pulled, not pulled, only local.</returns>
    private static IReadOnlyList<SwitchBranchItemViewModel> Branches() => new[]
    {
        new SwitchBranchItemViewModel(TestTexts.Russian, new GitSwitchableBranch("pulled", BranchSyncStatus.Pulled, 0, 0), false),
        new SwitchBranchItemViewModel(TestTexts.Russian, new GitSwitchableBranch("not-pulled", BranchSyncStatus.NotPulled, 0, 0), false),
        new SwitchBranchItemViewModel(TestTexts.Russian, new GitSwitchableBranch("local", BranchSyncStatus.LocalOnly, 0, 0), false)
    };
}
