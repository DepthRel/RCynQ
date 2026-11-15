using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using NSubstitute;
using RCynQ.Core.Enums;
using RCynQ.Core.State;
using RCynQ.UI.ViewModels.Items;
using RCynQ.UI.Views.Controls;
using static RCynQ.UI.Headless.Tests.HeadlessSession;

namespace RCynQ.UI.Headless.Tests;

/// <summary>
/// Tests of <see cref="ChangedRowsList"/>: with check marks on, a click on a file checks it, a double
/// click shows its diff and leaves the check mark as it was, and a file in work shows a ring.
/// </summary>
public sealed class ChangedRowsListTests
{
    /// <summary>
    /// A single click checks the file; a double click on it shows the diff once and leaves the check mark as it was,
    /// whether it was on or off.
    /// </summary>
    /// <param name="checkedBefore">Whether the file was checked before the double click.</param>
    /// <returns>A task that completes when the test is done.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task A_Double_Click_Shows_The_Diff_And_Keeps_The_Check_Mark(bool checkedBefore) => Value.Dispatch(
        () =>
        {
            var actions = Substitute.For<IChangedFileActions>();
            var file = new ChangedFileItemViewModel(new ChangedFile(ChangeKind.Modified, "a.txt", false), actions: actions) { IsSelectable = true };
            var window = new Window { Width = 400, Height = 200, Content = new ChangedRowsList { Rows = new[] { file } } };

            // Like the modal window of the diff, which opens on the second press: the release no longer reaches the list.
            actions.ShowDiffAsync(file).Returns(_ =>
            {
                window.IsEnabled = false;
                return Task.CompletedTask;
            });
            window.Show();
            try
            {
                Settle();
                var point = CenterOf(window, window.GetVisualDescendants().OfType<CheckBox>().Single(check => check.IsEffectivelyVisible));

                Click(window, point);
                Assert.True(file.IsSelected);
                actions.DidNotReceiveWithAnyArgs().ShowDiffAsync(default!);

                // A click away from the row, so the next two are a double click of their own.
                Click(window, new Point(390, 190));

                file.IsSelected = checkedBefore;
                Click(window, point);
                Click(window, point);
                window.IsEnabled = true;

                Assert.Equal(checkedBefore, file.IsSelected);
                actions.Received(1).ShowDiffAsync(file);
            }
            finally
            {
                window.Close();
            }

            return true;
        },
        CancellationToken.None);

    /// <summary>
    /// The row of a file shows a turning ring only while something is being done with the file.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public Task A_File_In_Work_Shows_A_Ring() => Value.Dispatch(
        () =>
        {
            var file = new ChangedFileItemViewModel(new ChangedFile(ChangeKind.Modified, "a.txt", false), actions: Substitute.For<IChangedFileActions>());
            var window = new Window { Width = 400, Height = 200, Content = new ChangedRowsList { Rows = new[] { file } } };
            window.Show();
            try
            {
                Settle();
                var ring = window.GetVisualDescendants().OfType<Arc>().Single(arc => arc.Classes.Contains("busy-ring"));
                Assert.False(ring.IsEffectivelyVisible);

                file.IsWorking = true;
                Settle();

                Assert.True(ring.IsEffectivelyVisible);
                Assert.True(ring.Bounds.Width > 0);
            }
            finally
            {
                window.Close();
            }

            return true;
        },
        CancellationToken.None);

    /// <summary>
    /// Finds the middle of a control in the coordinates of its window.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <param name="control">The control.</param>
    /// <returns>The point.</returns>
    private static Point CenterOf(Window window, Control control)
        => control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;

    /// <summary>
    /// Presses and releases the left mouse button at a point.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <param name="point">The point.</param>
    private static void Click(Window window, Point point)
    {
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Settle();
    }
}
