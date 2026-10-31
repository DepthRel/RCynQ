using Avalonia.Controls;
using Avalonia.VisualTree;
using RCynQ.Core.Enums;
using RCynQ.Core.State;
using RCynQ.UI.ViewModels.Dialogs;
using RCynQ.UI.Views.Dialogs;
using static RCynQ.UI.Headless.Tests.HeadlessSession;

namespace RCynQ.UI.Headless.Tests;

/// <summary>
/// Opens the real windows of patch files: their texts, check marks and rows are on screen.
/// </summary>
public sealed class PatchDialogsTests
{
    /// <summary>
    /// The window that saves a patch offers to keep the changes, checked, and the two buttons.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public Task Save_Window_Shows_The_Check_Mark_Checked() => Value.Dispatch(
        () =>
        {
            AppLocalizer.SetLanguage("ru");
            var window = new SavePatchDialog { DataContext = new SavePatchDialogViewModel(_ => Task.FromResult(true)) };
            window.Show();
            try
            {
                Settle();
                var check = window.GetVisualDescendants().OfType<CheckBox>().Single();
                Assert.True(check.IsChecked);

                var texts = TextsOf(window);
                Assert.Contains(AppLocalizer.Get("SavePatch.KeepChanges"), texts);
                Assert.Contains(AppLocalizer.Get("Common.Save"), texts);
                Assert.Contains(AppLocalizer.Get("Common.Cancel"), texts);
            }
            finally
            {
                window.Close();
                AppLocalizer.SetLanguage("en");
            }

            return true;
        },
        CancellationToken.None);

    /// <summary>
    /// The window that applies a patch shows the file rows with their check marks, and folder rows after the switch to the tree.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public Task Apply_Window_Shows_The_Files_Flat_And_As_A_Tree() => Value.Dispatch(
        () =>
        {
            var dialog = new ApplyPatchDialogViewModel(AppLocalizer, "changes.patch", new[]
            {
                new ChangedFile(ChangeKind.Modified, "src/app/a.cs", false),
                new ChangedFile(ChangeKind.Added, "src/app/b.cs", false),
                new ChangedFile(ChangeKind.Deleted, "readme.md", false)
            });
            var window = new ApplyPatchDialog { DataContext = dialog };
            window.Show();
            try
            {
                Settle();
                Assert.Contains("src/app/a.cs", TextsOf(window));
                Assert.Equal(3, window.GetVisualDescendants().OfType<CheckBox>().Count(check => check.IsVisible));

                dialog.ShowTreeCommand.Execute(null);
                Settle();

                var texts = TextsOf(window);
                Assert.Contains("src/app", texts);
                Assert.Contains("a.cs", texts);
                Assert.DoesNotContain("src/app/a.cs", texts);
            }
            finally
            {
                window.Close();
            }

            return true;
        },
        CancellationToken.None);

    /// <summary>
    /// Lists the texts on screen in a window.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <returns>The texts.</returns>
    private static IReadOnlyList<string> TextsOf(Window window)
        => window.GetVisualDescendants().OfType<TextBlock>().Where(block => block.IsVisible).Select(block => block.Text ?? string.Empty)
            .Concat(window.GetVisualDescendants().OfType<Button>().Select(button => button.Content as string ?? string.Empty))
            .ToArray();
}
