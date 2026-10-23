using FluentAssertions;
using RCynQ.Core.Enums;
using RCynQ.UI.Tests.Support;
using RCynQ.UI.ViewModels.Commits;

namespace RCynQ.UI.Tests.Pages;

/// <summary>
/// Tests of <see cref="UnsentCommitListViewModel"/>: the chosen commits always form one block from the oldest unsent commit.
/// </summary>
public sealed class UnsentCommitListViewModelTests
{
    /// <summary>
    /// Verifies that the rows are shown newest first and nothing is chosen at first.
    /// </summary>
    [Fact]
    public void Rows_Are_Newest_First_And_Nothing_Is_Chosen()
    {
        var list = Load();

        list.Items.Select(item => item.Subject).Should().Equal("five", "four", "three", "two", "one");
        list.SelectedCount.Should().Be(0);
        list.CanSend.Should().BeFalse();
        list.CanRollBack.Should().BeTrue();
        list.IsVisible.Should().BeTrue();
        list.CountText.Should().Be("(5 коммитов)");
    }

    /// <summary>
    /// Verifies that checking a commit checks every older one, so the block starts with the oldest unsent commit.
    /// </summary>
    [Fact]
    public void Checking_A_Commit_Checks_Every_Older_One()
    {
        var list = Load();

        list.Items[2].IsChecked = true;

        list.Items.Select(item => item.IsChecked).Should().Equal(false, false, true, true, true);
        list.SelectedTip.Should().Be(new string('3', 40));
        list.CanSend.Should().BeTrue();
        list.CountText.Should().Be("(выбрано 3 из 5)");
    }

    /// <summary>
    /// Verifies that clearing a commit clears every newer one: 1, 2, 3, 5 without 4 cannot be chosen.
    /// </summary>
    [Fact]
    public void Clearing_A_Commit_Clears_Every_Newer_One()
    {
        var list = Load();
        list.Items[0].IsChecked = true;

        list.Items[1].IsChecked = false;

        list.Items.Select(item => item.IsChecked).Should().Equal(false, false, true, true, true);
        list.SelectedTip.Should().Be(new string('3', 40));
    }

    /// <summary>
    /// Verifies that a new answer keeps the choice of the commits that are still unsent.
    /// </summary>
    [Fact]
    public void A_Refresh_Keeps_The_Choice_Of_Commits_Still_Unsent()
    {
        var list = Load();
        list.Items[3].IsChecked = true;

        list.Load(TestData.UnsentCommits(TestData.Commit('1', "one"), TestData.Commit('2', "two"), TestData.Commit('6', "six")));

        list.Items.Select(item => item.IsChecked).Should().Equal(false, true, true);
    }

    /// <summary>
    /// Verifies that a branch that diverged from the Origin shows its reason and offers nothing.
    /// </summary>
    [Fact]
    public void A_Diverged_Branch_Shows_Why_And_Offers_Nothing()
    {
        var list = new UnsentCommitListViewModel(TestTexts.Russian);

        list.Load(TestData.UnsentCommits() with { State = UnsentCommitsState.Diverged, Problem = "Сначала Pull." });

        list.IsVisible.Should().BeTrue();
        list.HasProblem.Should().BeTrue();
        list.Problem.Should().Be("Сначала Pull.");
        list.CanSend.Should().BeFalse();
        list.CanRollBack.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that a branch without unsent commits, or one the Origin does not have, hides the area.
    /// </summary>
    /// <param name="state">The state the service reports.</param>
    [Theory]
    [InlineData(UnsentCommitsState.Ready)]
    [InlineData(UnsentCommitsState.NoOriginBranch)]
    [InlineData(UnsentCommitsState.Detached)]
    public void Without_Local_Commits_The_Area_Is_Hidden(UnsentCommitsState state)
    {
        var list = new UnsentCommitListViewModel(TestTexts.Russian);

        list.Load(TestData.UnsentCommits() with { State = state, Problem = state == UnsentCommitsState.Ready ? null : "—" });

        list.IsVisible.Should().BeFalse();
        list.HasProblem.Should().BeFalse();
    }

    /// <summary>
    /// Creates a list of five unsent commits.
    /// </summary>
    /// <returns>The list.</returns>
    private static UnsentCommitListViewModel Load()
    {
        var list = new UnsentCommitListViewModel(TestTexts.Russian);
        list.Load(TestData.UnsentCommits(
            TestData.Commit('1', "one"),
            TestData.Commit('2', "two"),
            TestData.Commit('3', "three"),
            TestData.Commit('4', "four"),
            TestData.Commit('5', "five")));
        return list;
    }
}
