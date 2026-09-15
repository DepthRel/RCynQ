using CommunityToolkit.Mvvm.ComponentModel;

namespace RCynQ.UI.ViewModels.Navigation;

/// <summary>
/// One tab of the navigation bar.
/// </summary>
/// <remarks>
/// Every tab is always shown; tabs that do not apply to the node role are disabled. Tabs that do not fit the window
/// width are moved to the "…" menu by the view.
/// </remarks>
public sealed partial class NavigationItemViewModel : ObservableObject
{
    /// <summary>Whether the tab is selected.</summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Whether the tab applies to the node role.</summary>
    [ObservableProperty]
    private bool _isAvailable = true;

    /// <summary>Why the tab is unavailable, or <see langword="null"/>.</summary>
    [ObservableProperty]
    private string? _unavailableReason;

    /// <summary>Whether the tab signals a problem.</summary>
    [ObservableProperty]
    private bool _hasAttention;

    /// <summary>
    /// Initializes a new instance of the <see cref="NavigationItemViewModel"/> class.
    /// </summary>
    /// <param name="key">The tab key.</param>
    /// <param name="title">The tab title.</param>
    /// <param name="iconKey">The resource key of the icon geometry.</param>
    /// <param name="page">The page shown by the tab.</param>
    public NavigationItemViewModel(TabKey key, string title, string iconKey, PageViewModelBase page)
    {
        Key = key;
        Title = title;
        IconKey = iconKey;
        Page = page;
    }

    /// <summary>Gets the tab key.</summary>
    public TabKey Key { get; }

    /// <summary>Gets the tab title.</summary>
    public string Title { get; }

    /// <summary>Gets the resource key of the icon geometry.</summary>
    public string IconKey { get; }

    /// <summary>Gets the page shown by the tab.</summary>
    public PageViewModelBase Page { get; }
}
