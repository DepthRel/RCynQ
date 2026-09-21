using CommunityToolkit.Mvvm.ComponentModel;
using RCynQ.Core.Enums;
using RCynQ.Localization;

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

    /// <summary>The role that makes the tab unavailable, or <see langword="null"/> when the tab is available.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UnavailableReason))]
    private NodeRole? _unavailableForRole;

    /// <summary>Whether the tab signals a problem.</summary>
    [ObservableProperty]
    private bool _hasAttention;

    /// <summary>
    /// Initializes a new instance of the <see cref="NavigationItemViewModel"/> class.
    /// </summary>
    /// <param name="key">The tab key.</param>
    /// <param name="iconKey">The resource key of the icon geometry.</param>
    /// <param name="page">The page shown by the tab.</param>
    public NavigationItemViewModel(TabKey key, string iconKey, PageViewModelBase page)
    {
        Key = key;
        IconKey = iconKey;
        Page = page;
    }

    /// <summary>Gets the tab key.</summary>
    public TabKey Key { get; }

    /// <summary>Gets the tab title in the language of the user: the text <c>Nav.&lt;tab key&gt;</c>.</summary>
    public string Title => Texts.Get($"Nav.{Key}");

    /// <summary>Gets why the tab is unavailable, or <see langword="null"/> when it is available.</summary>
    public string? UnavailableReason => UnavailableForRole is { } role ? Texts.Format("Nav.UnavailableForRole", role) : null;

    /// <summary>Gets the resource key of the icon geometry.</summary>
    public string IconKey { get; }

    /// <summary>Gets the page shown by the tab.</summary>
    public PageViewModelBase Page { get; }

    /// <summary>
    /// Tells the bindings that the texts of the tab changed with the language.
    /// </summary>
    public void RefreshTexts() => OnPropertyChanged(string.Empty);
}
