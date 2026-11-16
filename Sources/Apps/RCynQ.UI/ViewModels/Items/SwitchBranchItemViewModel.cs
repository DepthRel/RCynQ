using RCynQ.Core.Enums;
using RCynQ.Core.State;
using RCynQ.Localization;

namespace RCynQ.UI.ViewModels.Items;

/// <summary>
/// A row of the branch switcher on the Branches tab: a branch and how it stands to the branches of the Origin.
/// </summary>
public sealed class SwitchBranchItemViewModel
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SwitchBranchItemViewModel"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the texts.</param>
    /// <param name="branch">The branch.</param>
    /// <param name="isCurrent">Whether the working tree is on this branch.</param>
    public SwitchBranchItemViewModel(ILocalizer localizer, GitSwitchableBranch branch, bool isCurrent)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(branch);

        Name = branch.Name;
        Status = branch.Status;
        IsCurrent = isCurrent;

        var status = localizer.Get(branch.Status switch
        {
            BranchSyncStatus.Pulled => "Branch.Status.Pulled",
            BranchSyncStatus.NotPulled => "Branch.Status.NotPulled",
            _ => "Branch.Status.LocalOnly"
        });
        var parts = new List<string> { status };
        if (branch.Ahead > 0 || branch.Behind > 0)
        {
            parts.Add(localizer.Format("Branch.Status.AheadBehind", branch.Ahead, branch.Behind));
        }

        if (isCurrent)
        {
            parts.Insert(0, localizer.Get("Branch.Status.Current"));
        }

        StatusText = string.Join(" · ", parts);
    }

    /// <summary>Gets the branch name.</summary>
    public string Name { get; }

    /// <summary>Gets how the branch stands to the branches of the Origin.</summary>
    public BranchSyncStatus Status { get; }

    /// <summary>Gets a value indicating whether the working tree is on this branch.</summary>
    public bool IsCurrent { get; }

    /// <summary>Gets the status in words, so the colour never carries the meaning alone.</summary>
    public string StatusText { get; }

    /// <summary>Gets a value indicating whether the branch is here and on the Origin.</summary>
    public bool IsPulled => Status == BranchSyncStatus.Pulled;

    /// <summary>Gets a value indicating whether the branch is on the Origin and not pulled yet.</summary>
    public bool IsNotPulled => Status == BranchSyncStatus.NotPulled;

    /// <summary>Gets a value indicating whether the branch is only on this computer.</summary>
    public bool IsLocalOnly => Status == BranchSyncStatus.LocalOnly;

    /// <summary>
    /// Tells whether the branch name contains a text, ignoring case.
    /// </summary>
    /// <param name="filter">The text; blank matches every branch.</param>
    /// <returns><see langword="true"/> when the branch is shown for the filter.</returns>
    public bool Matches(string? filter)
        => string.IsNullOrWhiteSpace(filter) || Name.Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase);
}
