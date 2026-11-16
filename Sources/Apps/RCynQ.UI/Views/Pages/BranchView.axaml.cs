using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using RCynQ.UI.ViewModels.Items;
using RCynQ.UI.ViewModels.Pages;

namespace RCynQ.UI.Views.Pages;

/// <summary>
/// The "Ветки" tab.
/// </summary>
public partial class BranchView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BranchView"/> class.
    /// </summary>
    public BranchView()
    {
        InitializeComponent();

        // Tunnelling, so the search box does not take Enter and Down for itself first.
        BranchSearchBox.AddHandler(KeyDownEvent, OnSearchKeyDown, RoutingStrategies.Tunnel);
        BranchList.AddHandler(KeyDownEvent, OnListKeyDown, RoutingStrategies.Tunnel);
    }

    /// <summary>
    /// Opens the switcher with an empty search and the cursor in it, so the user can type a part of the name at once.
    /// </summary>
    /// <param name="sender">The flyout.</param>
    /// <param name="e">The event data.</param>
    private void OnSwitchFlyoutOpened(object? sender, EventArgs e)
    {
        if (DataContext is BranchViewModel page)
        {
            page.BranchFilter = string.Empty;
        }

        BranchList.SelectedItem = null;
        BranchSearchBox.Focus();
    }

    /// <summary>
    /// Closes the panel of the status filter together with the switcher, so it never stays on screen without its list.
    /// </summary>
    /// <param name="sender">The flyout.</param>
    /// <param name="e">The event data.</param>
    private void OnSwitchFlyoutClosed(object? sender, EventArgs e)
    {
        if (DataContext is BranchViewModel page)
        {
            page.StatusFilter.IsOpen = false;
        }
    }

    /// <summary>
    /// Switches to the branch the user clicked.
    /// </summary>
    /// <param name="sender">The list.</param>
    /// <param name="e">The event data.</param>
    private void OnBranchTapped(object? sender, TappedEventArgs e)
    {
        if (BranchList.SelectedItem is SwitchBranchItemViewModel branch)
        {
            Choose(branch);
        }
    }

    /// <summary>
    /// Lets the keyboard work the switcher: Enter takes the first branch that matches the search, Down moves into the list.
    /// </summary>
    /// <param name="sender">The search box.</param>
    /// <param name="e">The event data.</param>
    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not BranchViewModel page || page.FilteredBranches.Count == 0)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            Choose(page.FilteredBranches[0]);
        }
        else if (e.Key == Key.Down)
        {
            e.Handled = true;
            BranchList.SelectedIndex = 0;
            BranchList.ContainerFromIndex(0)?.Focus();
        }
    }

    /// <summary>
    /// Takes Enter in the list as the choice of the selected branch.
    /// </summary>
    /// <param name="sender">The list.</param>
    /// <param name="e">The event data.</param>
    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && BranchList.SelectedItem is SwitchBranchItemViewModel branch)
        {
            e.Handled = true;
            Choose(branch);
        }
    }

    /// <summary>
    /// Closes the switcher and switches the working tree to a branch.
    /// </summary>
    /// <param name="branch">The branch.</param>
    private void Choose(SwitchBranchItemViewModel branch)
    {
        SwitchButton.Flyout?.Hide();
        BranchList.SelectedItem = null;
        if (DataContext is BranchViewModel page && page.SwitchToCommand.CanExecute(branch))
        {
            page.SwitchToCommand.Execute(branch);
        }
    }
}
