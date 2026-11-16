using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RCynQ.Core.Enums;
using RCynQ.UI.ViewModels.Items;

namespace RCynQ.UI.ViewModels;

/// <summary>
/// The filter of the branch switcher by the state of a branch, on two independent axes: whether the branch is on this
/// computer (pulled or not) and whether the Origin has it (on the Origin or only local).
/// </summary>
/// <remarks>
/// Inside one axis the checked choices add up; the two axes narrow each other. An axis with nothing checked does not
/// narrow the list, so the filter starts, and is reset, with everything unchecked and every branch shown. A change of a
/// check mark takes effect at once; the panel closes only by its buttons or a click outside it.
/// </remarks>
public sealed partial class BranchStatusFilterViewModel : ObservableObject
{
    /// <summary>Whether a check mark is set from <see cref="Reset"/>, so the list is filtered once at the end.</summary>
    private bool _isResetting;

    /// <summary>Whether branches that are on this computer are shown: pulled from the Origin, or only local.</summary>
    [ObservableProperty]
    private bool _showPulled;

    /// <summary>Whether branches that are not on this computer yet are shown: on the Origin, not pulled.</summary>
    [ObservableProperty]
    private bool _showNotPulled;

    /// <summary>Whether branches the Origin has are shown: pulled or not.</summary>
    [ObservableProperty]
    private bool _showOnOrigin;

    /// <summary>Whether branches only this computer has are shown.</summary>
    [ObservableProperty]
    private bool _showLocalOnly;

    /// <summary>Whether the panel of the filter is open.</summary>
    [ObservableProperty]
    private bool _isOpen;

    /// <summary>
    /// Raised when the branches the filter lets through change.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>Gets a value indicating whether any check mark narrows the list.</summary>
    public bool IsActive => ShowPulled || ShowNotPulled || ShowOnOrigin || ShowLocalOnly;

    /// <summary>
    /// Tells whether a branch passes the filter.
    /// </summary>
    /// <param name="branch">The branch.</param>
    /// <returns><see langword="true"/> when the branch is shown.</returns>
    public bool Matches(SwitchBranchItemViewModel branch)
    {
        ArgumentNullException.ThrowIfNull(branch);

        var isHere = branch.Status != BranchSyncStatus.NotPulled;
        var isOnOrigin = branch.Status != BranchSyncStatus.LocalOnly;

        var passesPresence = (!ShowPulled && !ShowNotPulled) || (ShowPulled && isHere) || (ShowNotPulled && !isHere);
        var passesLocation = (!ShowOnOrigin && !ShowLocalOnly) || (ShowOnOrigin && isOnOrigin) || (ShowLocalOnly && !isOnOrigin);
        return passesPresence && passesLocation;
    }

    /// <summary>
    /// Keeps the check marks as they are and closes the panel: they already took effect.
    /// </summary>
    [RelayCommand]
    private void Apply() => IsOpen = false;

    /// <summary>
    /// Clears every check mark, so every branch is shown again, and closes the panel.
    /// </summary>
    [RelayCommand]
    private void Reset()
    {
        _isResetting = true;
        try
        {
            ShowPulled = false;
            ShowNotPulled = false;
            ShowOnOrigin = false;
            ShowLocalOnly = false;
        }
        finally
        {
            _isResetting = false;
        }

        OnFilterChanged();
        IsOpen = false;
    }

    /// <summary>
    /// Takes a change of the presence on this computer into account.
    /// </summary>
    /// <param name="value">The new value.</param>
    partial void OnShowPulledChanged(bool value) => OnFilterChanged();

    /// <summary>
    /// Takes a change of the absence from this computer into account.
    /// </summary>
    /// <param name="value">The new value.</param>
    partial void OnShowNotPulledChanged(bool value) => OnFilterChanged();

    /// <summary>
    /// Takes a change of the presence on the Origin into account.
    /// </summary>
    /// <param name="value">The new value.</param>
    partial void OnShowOnOriginChanged(bool value) => OnFilterChanged();

    /// <summary>
    /// Takes a change of the branches only this computer has into account.
    /// </summary>
    /// <param name="value">The new value.</param>
    partial void OnShowLocalOnlyChanged(bool value) => OnFilterChanged();

    /// <summary>
    /// Tells the owner the filter changed, unless a reset is still setting the check marks.
    /// </summary>
    private void OnFilterChanged()
    {
        if (_isResetting)
        {
            return;
        }

        OnPropertyChanged(nameof(IsActive));
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
