using Avalonia.Controls;
using Avalonia.VisualTree;
using RCynQ.Core.Api;
using RCynQ.Core.Enums;
using RCynQ.UI.ViewModels.Dialogs;
using RCynQ.UI.Views.Dialogs;
using static RCynQ.UI.Headless.Tests.HeadlessSession;

namespace RCynQ.UI.Headless.Tests;

/// <summary>
/// Opens the real window of the diff of a changed file: its changes, lines and check marks are on screen, and a long
/// diff opens with only the visible rows built.
/// </summary>
public sealed class FileDiffDialogTests
{
    /// <summary>
    /// The window shows the head of every change with its check mark, the lines with their numbers and the buttons.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public Task Changes_Lines_And_Check_Marks_Are_Shown() => Value.Dispatch(
        () =>
        {
            var dialog = new FileDiffDialogViewModel(AppLocalizer, Diff(2, 2), NodeRole.Origin);
            var window = new FileDiffDialog { DataContext = dialog };
            window.Show();
            try
            {
                Settle();
                var texts = window.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).Select(text => text.Text).ToList();
                Assert.Contains(AppLocalizer.Format("Diff.Change", 1, 2, 1), texts);
                Assert.Contains("line 0-1", texts);
                Assert.Contains("+", texts);
                Assert.Contains(AppLocalizer.Get("Diff.Apply"), texts);
                Assert.Equal(2, window.GetVisualDescendants().OfType<CheckBox>().Count(check => check.IsEffectivelyVisible));
            }
            finally
            {
                window.Close();
            }

            return true;
        },
        CancellationToken.None);

    /// <summary>
    /// A diff of thousands of lines builds only the rows on screen.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public Task A_Long_Diff_Builds_Only_The_Visible_Rows() => Value.Dispatch(
        () =>
        {
            var window = new FileDiffDialog { DataContext = new FileDiffDialogViewModel(AppLocalizer, Diff(200, 50), NodeRole.Origin) };
            window.Show();
            try
            {
                Settle();
                var built = window.GetVisualDescendants().OfType<TextBlock>().Count(text => text.Text?.StartsWith("line ", StringComparison.Ordinal) == true);
                Assert.InRange(built, 1, 500);
            }
            finally
            {
                window.Close();
            }

            return true;
        },
        CancellationToken.None);

    /// <summary>
    /// Builds a diff of added lines.
    /// </summary>
    /// <param name="blocks">The number of changes.</param>
    /// <param name="lines">The number of lines of each change.</param>
    /// <returns>The diff.</returns>
    private static FileDiffResponse Diff(int blocks, int lines)
        => new(
            "a.txt",
            DiffAvailability.Text,
            PartialChangeState.None,
            true,
            false,
            "base",
            "work",
            Enumerable.Range(0, blocks)
                .Select(block => new DiffBlockDto(
                    block,
                    block * lines,
                    0,
                    block * lines,
                    lines,
                    true,
                    Enumerable.Range(0, lines).Select(line => new DiffLineDto(DiffLineKind.Added, null, (block * lines) + line + 1, $"line {block}-{line}")).ToArray()))
                .ToArray());
}
