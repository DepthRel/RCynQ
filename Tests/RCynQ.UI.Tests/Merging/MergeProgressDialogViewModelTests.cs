using FluentAssertions;
using RCynQ.UI.Enums;
using RCynQ.UI.Services.Merging;
using RCynQ.UI.ViewModels.Dialogs;

namespace RCynQ.UI.Tests.Merging;

/// <summary>
/// Tests of the window of a merge in a merge tool.
/// </summary>
public sealed class MergeProgressDialogViewModelTests
{
    /// <summary>
    /// Verifies that a question shows its answers as buttons, without the one the window always has, and that a button
    /// answers it.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task A_Question_Is_Answered_By_Its_Buttons()
    {
        var dialog = new MergeProgressDialogViewModel(TestTexts.Russian, "KDiff3", (_, _) => Task.FromResult(ExternalMergeOutcome.Cancelled));

        var answer = dialog.AskAsync("Файл не сохранён", new[] { MergePromptChoice.CheckAgain, MergePromptChoice.OpenAgain, MergePromptChoice.Cancel });

        dialog.IsAsking.Should().BeTrue();
        dialog.Title.Should().Contain("KDiff3");
        dialog.Options.Select(option => option.Choice).Should().Equal(MergePromptChoice.CheckAgain, MergePromptChoice.OpenAgain);
        dialog.Options[0].Text.Should().Be("Проверить снова");
        dialog.Options[1].ChooseCommand.Execute(null);
        (await answer).Should().Be(MergePromptChoice.OpenAgain);
        dialog.IsAsking.Should().BeFalse();
        dialog.Options.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that giving up answers a waiting question with "cancel" and cancels the merge.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Giving_Up_Answers_The_Question_And_Cancels_The_Merge()
    {
        CancellationToken token = default;
        var asked = new TaskCompletionSource<Task<MergePromptChoice>>();
        var dialog = new MergeProgressDialogViewModel(TestTexts.Russian, "Meld", async (progress, cancel) =>
        {
            token = cancel;
            var answer = progress.AskAsync("?", new[] { MergePromptChoice.Retry, MergePromptChoice.Cancel });
            asked.SetResult(answer);
            return await answer == MergePromptChoice.Cancel ? ExternalMergeOutcome.Cancelled : ExternalMergeOutcome.Failed("unexpected");
        });
        bool? closed = null;
        dialog.CloseRequested += (_, confirmed) => closed = confirmed;

        var run = dialog.RunAsync();
        await asked.Task;
        dialog.CancelCommand.Execute(null);
        await run;

        token.IsCancellationRequested.Should().BeTrue();
        dialog.Outcome.Should().Be(ExternalMergeOutcome.Cancelled);
        dialog.IsFinished.Should().BeTrue();
        closed.Should().BeTrue();
        (await dialog.AskAsync("later", new[] { MergePromptChoice.Retry })).Should().Be(MergePromptChoice.Cancel, "a merge given up asks nothing more");
    }

    /// <summary>
    /// Verifies that the window shows the file and closes with the outcome when the merge ends.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task The_Window_Closes_With_The_Outcome()
    {
        var failed = ExternalMergeOutcome.Failed("broken");
        var dialog = new MergeProgressDialogViewModel(TestTexts.Russian, "VS Code", (progress, _) =>
        {
            progress.ShowFile(2, 3, "src/app.cs");
            return Task.FromResult(failed);
        });

        await dialog.RunAsync();

        dialog.FileText.Should().Be("Файл 2 из 3: src/app.cs");
        dialog.Outcome.Should().Be(failed);
        dialog.IsFinished.Should().BeTrue();
    }
}
