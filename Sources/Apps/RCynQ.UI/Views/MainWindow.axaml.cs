using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using RCynQ.UI.Controls;
using RCynQ.UI.ViewModels;
using RCynQ.UI.ViewModels.Navigation;

namespace RCynQ.UI.Views;

/// <summary>
/// The main window with the tab strip.
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>The tabs that currently do not fit the tab strip.</summary>
    private IReadOnlyList<NavigationItemViewModel> _overflowTabs = Array.Empty<NavigationItemViewModel>();

    /// <summary>The view model whose selection is observed.</summary>
    private MainViewModel? _viewModel;

    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindow"/> class.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
        AddHandler(OverflowStackPanel.OverflowChangedEvent, OnTabsOverflowChanged);
    }

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as MainViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    /// <summary>
    /// Updates the overflow button when the tab strip shows a different number of tabs.
    /// </summary>
    /// <param name="sender">The window.</param>
    /// <param name="e">The event arguments; the source is the tab panel.</param>
    private void OnTabsOverflowChanged(object? sender, RoutedEventArgs e)
    {
        if (e.Source is not OverflowStackPanel panel)
        {
            return;
        }

        _overflowTabs = panel.Children
            .Skip(Math.Max(panel.VisibleCount, 0))
            .Select(child => child.DataContext)
            .OfType<NavigationItemViewModel>()
            .ToArray();
        OverflowButton.IsVisible = _overflowTabs.Count > 0;
        UpdateOverflowSelection();
    }

    /// <summary>
    /// Highlights the overflow button when the selected tab is in the overflow menu.
    /// </summary>
    /// <param name="sender">The view model.</param>
    /// <param name="e">The event arguments.</param>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedTab))
        {
            UpdateOverflowSelection();
        }
    }

    /// <summary>
    /// Sets the selected state of the overflow button.
    /// </summary>
    private void UpdateOverflowSelection()
        => OverflowButton.Classes.Set("selected", _viewModel?.SelectedTab is { } selected && _overflowTabs.Contains(selected));

    /// <summary>
    /// Opens the "…" menu with the tabs that do not fit the strip.
    /// </summary>
    /// <param name="sender">The overflow button.</param>
    /// <param name="e">The event arguments.</param>
    private void OnOverflowClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null || sender is not Control target || _overflowTabs.Count == 0)
        {
            return;
        }

        var flyout = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
        foreach (var tab in _overflowTabs)
        {
            flyout.Items.Add(new MenuItem
            {
                Header = tab.Title,
                Icon = new IconView { IconKey = tab.IconKey, Width = 16, Height = 16 },
                IsEnabled = tab.IsAvailable,
                Command = _viewModel.SelectTabCommand,
                CommandParameter = tab
            });
        }

        flyout.ShowAt(target);
    }
}
