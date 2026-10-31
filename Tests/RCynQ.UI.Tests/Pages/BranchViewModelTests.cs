using FluentAssertions;
using NSubstitute;
using RCynQ.Core.Api;
using RCynQ.UI.Services.Api;
using RCynQ.UI.Services.Dialogs;
using RCynQ.UI.ViewModels.Pages;

namespace RCynQ.UI.Tests.Pages;

/// <summary>
/// Tests of the messages <see cref="BranchViewModel"/> shows after Pull and Fetch.
/// </summary>
public sealed class BranchViewModelTests
{
    /// <summary>The API substitute.</summary>
    private readonly IServiceApiClient _api = Substitute.For<IServiceApiClient>();

    /// <summary>The dialog substitute.</summary>
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();

    /// <summary>
    /// Verifies that a Pull that succeeded shows a green message and a failed one a red message, and that each action
    /// hides the message of the previous one.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Pull_Shows_A_Green_Or_A_Red_Message()
    {
        var branch = new BranchViewModel(TestTexts.Russian, _api, _dialogs);
        _api.PullAsync(Arg.Any<CancellationToken>()).Returns(new GitOperationResult(true, "Fast-forward", null));

        await branch.PullCommand.ExecuteAsync(null);

        branch.SuccessMessage.Should().Be(TestTexts.Russian.Get("Branch.PullSucceeded"));
        branch.HasSuccess.Should().BeTrue();
        branch.HasError.Should().BeFalse();
        branch.Output.Should().Be("Fast-forward");

        _api.PullAsync(Arg.Any<CancellationToken>()).Returns(new GitOperationResult(false, "fatal: not possible to fast-forward", null));

        await branch.PullCommand.ExecuteAsync(null);

        branch.ErrorMessage.Should().Be(TestTexts.Russian.Get("Branch.PullFailed"));
        branch.HasSuccess.Should().BeFalse("the message of the previous Pull no longer applies");
    }

    /// <summary>
    /// Verifies that a Fetch that succeeded shows a green message and a failed one a red message.
    /// </summary>
    /// <param name="succeeded">Whether the Fetch succeeded.</param>
    /// <returns>A task representing the test.</returns>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Fetch_Shows_A_Green_Or_A_Red_Message(bool succeeded)
    {
        var branch = new BranchViewModel(TestTexts.Russian, _api, _dialogs);
        _api.FetchAsync(Arg.Any<CancellationToken>()).Returns(new GitOperationResult(succeeded, "output", null));

        await branch.FetchCommand.ExecuteAsync(null);

        branch.HasSuccess.Should().Be(succeeded);
        branch.HasError.Should().Be(!succeeded);
        (succeeded ? branch.SuccessMessage : branch.ErrorMessage)
            .Should().Be(TestTexts.Russian.Get(succeeded ? "Branch.FetchSucceeded" : "Branch.FetchFailed"));
    }

    /// <summary>
    /// Verifies that hiding the messages hides the green one too.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Dismissing_Hides_The_Green_Message()
    {
        var branch = new BranchViewModel(TestTexts.Russian, _api, _dialogs);
        _api.FetchAsync(Arg.Any<CancellationToken>()).Returns(new GitOperationResult(true, "output", null));
        await branch.FetchCommand.ExecuteAsync(null);

        branch.DismissMessagesCommand.Execute(null);

        branch.HasSuccess.Should().BeFalse();
    }
}
