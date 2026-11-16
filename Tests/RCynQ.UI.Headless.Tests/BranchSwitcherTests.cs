using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using NSubstitute;
using RCynQ.Core.Api;
using RCynQ.Core.Enums;
using RCynQ.Core.State;
using RCynQ.UI.Services.Api;
using RCynQ.UI.Services.Dialogs;
using RCynQ.UI.ViewModels.Dialogs;
using RCynQ.UI.ViewModels.Pages;
using RCynQ.UI.Views.Dialogs;
using RCynQ.UI.Views.Pages;
using static RCynQ.UI.Headless.Tests.HeadlessSession;

namespace RCynQ.UI.Headless.Tests;

/// <summary>
/// Opens the real branch switcher of the Branches tab and its dialog: the search, the keyboard and the texts on screen.
/// </summary>
public sealed class BranchSwitcherTests
{
    /// <summary>
    /// Typing a part of a name leaves only the matching branches, and Enter switches to the first of them and closes the list.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public Task Search_And_Enter_Switch_To_The_Matching_Branch() => Value.Dispatch(
        async () =>
        {
            var api = Substitute.For<IServiceApiClient>();
            api.GetBranchAsync(Arg.Any<CancellationToken>()).Returns(new GitBranchInfo(true, "main", null, 0, 0));
            api.GetSwitchableBranchesAsync(Arg.Any<CancellationToken>()).Returns(new GitSwitchBranchesResponse(
                "main",
                "main",
                new[]
                {
                    new GitSwitchableBranch("feature/login", BranchSyncStatus.NotPulled, 0, 0),
                    new GitSwitchableBranch("main", BranchSyncStatus.Pulled, 0, 0)
                }));
            api.SwitchBranchAsync(Arg.Any<SwitchBranchRequest>(), Arg.Any<CancellationToken>())
                .Returns(new SwitchBranchResult(BranchSwitchOutcome.Switched, "switched", Array.Empty<string>(), null));
            var page = new BranchViewModel(AppLocalizer, api, Substitute.For<IDialogService>());
            page.Configure(RoleCapabilities.For(NodeRole.Workstation));
            await page.EnsureLoadedAsync();

            var view = new BranchView { DataContext = page };
            var window = new Window { Content = view };
            window.Show();
            try
            {
                Settle();
                var button = view.GetVisualDescendants().OfType<Button>().Single(candidate => candidate.Name == "SwitchButton");
                Assert.True(button.IsVisible);

                var flyout = (Flyout)button.Flyout!;
                flyout.ShowAt(button);
                Settle();
                var content = (Control)flyout.Content!;
                var search = content.GetLogicalDescendants().OfType<TextBox>().Single();
                search.Text = "LOG";
                Settle();
                Assert.Equal(new[] { "feature/login" }, page.FilteredBranches.Select(item => item.Name));

                search.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter, Source = search });
                Settle();

                await api.Received(1).SwitchBranchAsync(Arg.Is<SwitchBranchRequest>(request => request.Branch == "feature/login"), Arg.Any<CancellationToken>());
                Assert.False(flyout.IsOpen);
            }
            finally
            {
                window.Close();
            }

            return true;
        },
        CancellationToken.None);

    /// <summary>
    /// The dialog lists the changed files and offers to take the changes along or save them, and nothing that throws them away.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public Task The_Dialog_Lists_The_Files_And_Both_Choices() => Value.Dispatch(
        () =>
        {
            var dialog = new SwitchBranchDialogViewModel(AppLocalizer, "feature/login", new[] { "src/a.cs", "readme.md" }, () => Task.FromResult<string?>(null));
            var window = new SwitchBranchDialog { DataContext = dialog };
            window.Show();
            try
            {
                Settle();
                var texts = window.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text).ToArray();
                Assert.Contains("src/a.cs", texts);
                Assert.Contains("readme.md", texts);
                Assert.Contains(AppLocalizer.Get("BranchSwitch.Dialog.Carry"), texts);
                Assert.Contains(AppLocalizer.Get("BranchSwitch.Dialog.SavePatch"), texts);
                Assert.Contains(AppLocalizer.Format("BranchSwitch.Dialog.Hint", "feature/login", 2), texts);
                Assert.Equal(3, window.GetVisualDescendants().OfType<Button>().Count());
            }
            finally
            {
                window.Close();
            }

            return true;
        },
        CancellationToken.None);

    /// <summary>
    /// The save button of the real dialog asks for the file while the dialog is open, and closes it with the file chosen.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public Task The_Save_Button_Asks_For_The_File_And_Closes_The_Dialog() => Value.Dispatch(
        async () =>
        {
            var owner = new Window();
            owner.Show();
            var askedWhileOpen = false;
            SwitchBranchDialog? window = null;
            var dialog = new SwitchBranchDialogViewModel(AppLocalizer, "feature/login", new[] { "src/a.cs" }, () =>
            {
                askedWhileOpen = window!.IsVisible;
                return Task.FromResult<string?>("C:/patches/feature-login-changes.patch");
            });
            window = new SwitchBranchDialog { DataContext = dialog };
            try
            {
                var shown = window.ShowDialog<bool>(owner);
                Settle();
                var save = window.GetVisualDescendants().OfType<Button>().Single(button => ReferenceEquals(button.Command, dialog.SavePatchCommand));
                save.Command!.Execute(null);
                Settle();

                Assert.True(await shown);
                Assert.True(askedWhileOpen);
                Assert.Equal(BranchSwitchMode.SavePatch, dialog.Result);
                Assert.Equal("C:/patches/feature-login-changes.patch", dialog.PatchPath);
            }
            finally
            {
                owner.Close();
            }

            return true;
        },
        CancellationToken.None);

    /// <summary>
    /// The dialog after the switch names the branch and the file the changes are in, and offers to bring them back or not now.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public Task The_Restore_Dialog_Names_The_File_And_Both_Choices() => Value.Dispatch(
        () =>
        {
            var dialog = new RestoreChangesDialogViewModel(AppLocalizer, "feature/login", "C:/patches/feature-login-changes.patch");
            var window = new RestoreChangesDialog { DataContext = dialog };
            window.Show();
            try
            {
                Settle();
                var texts = window.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text).ToArray();
                Assert.Contains(AppLocalizer.Format("BranchSwitch.Restore.Hint", "feature/login"), texts);
                Assert.Contains("C:/patches/feature-login-changes.patch", texts);
                Assert.Contains(AppLocalizer.Get("BranchSwitch.Restore.Apply"), texts);
                Assert.Contains(AppLocalizer.Get("BranchSwitch.Restore.Later"), texts);
                Assert.Equal(2, window.GetVisualDescendants().OfType<Button>().Count());
            }
            finally
            {
                window.Close();
            }

            return true;
        },
        CancellationToken.None);

    /// <summary>
    /// The filter button opens its panel next to the list; a check mark narrows the list at once and leaves both open; a
    /// click elsewhere in the list, the filter button itself, Apply and Reset close only the panel, never the list.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public Task The_Status_Filter_Panel_Closes_Without_The_List() => Value.Dispatch(
        async () =>
        {
            var api = Substitute.For<IServiceApiClient>();
            api.GetBranchAsync(Arg.Any<CancellationToken>()).Returns(new GitBranchInfo(true, "main", null, 0, 0));
            api.GetSwitchableBranchesAsync(Arg.Any<CancellationToken>()).Returns(new GitSwitchBranchesResponse(
                "main",
                "main",
                new[]
                {
                    new GitSwitchableBranch("experiment", BranchSyncStatus.LocalOnly, 0, 0),
                    new GitSwitchableBranch("feature/login", BranchSyncStatus.NotPulled, 0, 0),
                    new GitSwitchableBranch("main", BranchSyncStatus.Pulled, 0, 0)
                }));
            var page = new BranchViewModel(AppLocalizer, api, Substitute.For<IDialogService>());
            page.Configure(RoleCapabilities.For(NodeRole.Workstation));
            await page.EnsureLoadedAsync();

            var view = new BranchView { DataContext = page };
            var window = new Window { Content = view };
            window.Show();
            try
            {
                Settle();
                var button = view.GetVisualDescendants().OfType<Button>().Single(candidate => candidate.Name == "SwitchButton");
                var flyout = (Flyout)button.Flyout!;
                flyout.ShowAt(button);
                Settle();
                var content = (Control)flyout.Content!;
                var toggle = content.GetLogicalDescendants().OfType<ToggleButton>().Single(candidate => candidate.Name == "StatusFilterButton");
                var search = content.GetLogicalDescendants().OfType<TextBox>().Single();
                var popup = content.GetLogicalDescendants().OfType<Popup>().Single(candidate => candidate.Name == "StatusFilterPopup");

                ClickAt(toggle);
                Assert.True(popup.IsOpen);
                Assert.True(toggle.IsChecked);
                var panel = (Control)popup.Child!;
                var localOnly = panel.GetLogicalDescendants().OfType<CheckBox>().Single(candidate => candidate.Name == "FilterLocalOnly");

                ClickAt(localOnly);
                Assert.Equal(new[] { "experiment" }, page.FilteredBranches.Select(item => item.Name));
                Assert.True(popup.IsOpen, "a check mark takes effect without closing the panel");
                Assert.True(flyout.IsOpen);

                ClickAt(Buttons(panel).Single(candidate => ReferenceEquals(candidate.Command, page.StatusFilter.ApplyCommand)));
                Assert.False(popup.IsOpen);
                Assert.True(flyout.IsOpen, "Apply closes only the panel");
                Assert.True(page.StatusFilter.ShowLocalOnly);

                ClickAt(toggle);
                Assert.True(popup.IsOpen);
                ClickAt(search);
                Assert.False(popup.IsOpen);
                Assert.True(flyout.IsOpen, "a click outside the panel closes only the panel");

                ClickAt(toggle);
                Assert.True(popup.IsOpen);
                ClickAt(toggle);
                Assert.False(popup.IsOpen, "the pressed-in button closes its panel and does not open it again");
                Assert.True(flyout.IsOpen);

                ClickAt(toggle);
                ClickAt(Buttons(panel).Single(candidate => ReferenceEquals(candidate.Command, page.StatusFilter.ResetCommand)));
                Assert.False(popup.IsOpen);
                Assert.True(flyout.IsOpen, "Reset closes only the panel");
                Assert.False(page.StatusFilter.IsActive);
                Assert.Equal(3, page.FilteredBranches.Count);
                await api.DidNotReceiveWithAnyArgs().SwitchBranchAsync(default!, default);

                ClickAt(toggle);
                Assert.True(popup.IsOpen);
                flyout.Hide();
                Settle();
                Assert.False(popup.IsOpen, "the panel never stays on screen without its list");
                Assert.False(page.StatusFilter.IsOpen);
            }
            finally
            {
                window.Close();
            }

            return true;
        },
        CancellationToken.None);

    /// <summary>
    /// Finds the buttons of a panel.
    /// </summary>
    /// <param name="panel">The panel.</param>
    /// <returns>The buttons.</returns>
    private static IEnumerable<Button> Buttons(Control panel) => panel.GetLogicalDescendants().OfType<Button>();

    /// <summary>
    /// Presses and releases the left mouse button in the middle of a control, on the window or popup it is shown in.
    /// A control that is not on screen gets no click.
    /// </summary>
    /// <param name="control">The control.</param>
    private static void ClickAt(Control control)
    {
        if (TopLevel.GetTopLevel(control) is not { } top || !control.IsEffectivelyVisible)
        {
            return;
        }

        var position = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), top)!.Value;
        top.MouseDown(position, MouseButton.Left);
        top.MouseUp(position, MouseButton.Left);
        Settle();
    }
}
