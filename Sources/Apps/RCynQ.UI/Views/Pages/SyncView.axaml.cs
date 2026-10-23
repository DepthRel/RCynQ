using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using RCynQ.UI.ViewModels.Pages;

namespace RCynQ.UI.Views.Pages;

/// <summary>
/// The "Синхронизация" tab.
/// </summary>
public partial class SyncView : UserControl
{
    /// <summary>How close to the end of the page, in device-independent pixels, scrolling starts loading older patches.</summary>
    private const double LOAD_MORE_DISTANCE = 120;

    /// <summary>
    /// Initializes a new instance of the <see cref="SyncView"/> class.
    /// </summary>
    public SyncView()
    {
        InitializeComponent();
        PageScroll.ScrollChanged += OnPageScrollChanged;
        SearchBox.PropertyChanged += OnSearchBoxPropertyChanged;
    }

    /// <summary>
    /// Loads older patches when the expanded history is scrolled to its end, so the user does not have to press the button.
    /// </summary>
    /// <param name="sender">The scroll viewer of the page.</param>
    /// <param name="e">The event data.</param>
    private void OnPageScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (DataContext is not SyncViewModel { CanLoadMoreHistory: true, IsLoadingMoreHistory: false } sync)
        {
            return;
        }

        var distance = PageScroll.Extent.Height - PageScroll.Viewport.Height - PageScroll.Offset.Y;
        if (distance <= LOAD_MORE_DISTANCE)
        {
            sync.LoadMoreHistoryCommand.Execute(null);
        }
    }

    /// <summary>
    /// Puts the cursor into the search line as soon as it is shown, so the user can type at once.
    /// </summary>
    /// <param name="sender">The search line.</param>
    /// <param name="e">The event data.</param>
    private void OnSearchBoxPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == IsVisibleProperty && SearchBox.IsVisible)
        {
            Dispatcher.UIThread.Post(() => SearchBox.Focus());
        }
    }
}
