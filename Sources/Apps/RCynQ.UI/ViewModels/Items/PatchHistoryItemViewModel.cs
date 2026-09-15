using RCynQ.Core.Enums;
using RCynQ.Core.Patches;
using RCynQ.UI.ViewModels.Formatting;

namespace RCynQ.UI.ViewModels.Items;

/// <summary>
/// A row of the patch history on the "Синхронизация" tab.
/// </summary>
public sealed class PatchHistoryItemViewModel
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PatchHistoryItemViewModel"/> class.
    /// </summary>
    /// <param name="patch">The patch metadata.</param>
    public PatchHistoryItemViewModel(PatchMetadata patch)
    {
        Patch = patch ?? throw new ArgumentNullException(nameof(patch));
    }

    /// <summary>Gets the patch metadata.</summary>
    public PatchMetadata Patch { get; }

    /// <summary>Gets the patch identifier.</summary>
    public Guid Id => Patch.Id;

    /// <summary>Gets the creation time.</summary>
    public string TimeText => DisplayText.Time(Patch.CreatedAtUtc);

    /// <summary>Gets the direction label.</summary>
    public string DirectionText => DisplayText.Direction(Patch.Direction);

    /// <summary>Gets the number of files.</summary>
    public int FileCount => Patch.Manifest.Count;

    /// <summary>Gets the status label.</summary>
    public string StatusText => DisplayText.Patch(Patch.Status);

    /// <summary>Gets a value indicating whether the patch succeeded.</summary>
    public bool IsSuccess => Patch.Status is PatchStatus.Applied or PatchStatus.ConflictResolved;

    /// <summary>Gets a value indicating whether the patch needs attention.</summary>
    public bool IsProblem => Patch.Status is PatchStatus.ConflictPending or PatchStatus.Failed;

    /// <summary>Gets a value indicating whether the patch can be resolved on this node.</summary>
    public bool CanResolve => Patch.Direction == PatchDirection.Incoming && Patch.Status is PatchStatus.ConflictPending or PatchStatus.Failed;
}
