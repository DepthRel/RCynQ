using CommunityToolkit.Mvvm.ComponentModel;
using RCynQ.Core.Enums;
using RCynQ.Core.Patches;
using RCynQ.Localization;
using RCynQ.UI.ViewModels.Formatting;

namespace RCynQ.UI.ViewModels.Items;

/// <summary>
/// A row of the patch history on the "Синхронизация" tab.
/// </summary>
public sealed class PatchHistoryItemViewModel : ObservableObject
{
    /// <summary>The localizer of the messages.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="PatchHistoryItemViewModel"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="patch">The patch metadata.</param>
    public PatchHistoryItemViewModel(ILocalizer localizer, PatchMetadata patch)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        Patch = patch ?? throw new ArgumentNullException(nameof(patch));
    }

    /// <summary>Gets the patch metadata.</summary>
    public PatchMetadata Patch { get; }

    /// <summary>Gets the patch identifier.</summary>
    public Guid Id => Patch.Id;

    /// <summary>Gets the creation time.</summary>
    public string TimeText => DisplayText.Time(_localizer, Patch.CreatedAtUtc);

    /// <summary>Gets the direction label.</summary>
    public string DirectionText => DisplayText.Direction(_localizer, Patch.Direction);

    /// <summary>Gets the number of files.</summary>
    public int FileCount => Patch.Manifest.Count;

    /// <summary>Gets a value indicating whether the patch delivered commits rather than files.</summary>
    public bool IsCommits => Patch.Commits is not null;

    /// <summary>Gets what the patch carried: the number of files, or of commits.</summary>
    public string ContentText => Patch.Commits is { } commits
        ? DisplayText.Count(_localizer, "Commits.Count", commits.Commits.Count)
        : DisplayText.Count(_localizer, "Sync.FileCount", FileCount);

    /// <summary>Gets the tool tip of the button that shows what the patch carried: its diff, or its commits.</summary>
    public string DiffTip => _localizer.Get(IsCommits ? "Commits.ShowList" : "Sync.ShowDiff");

    /// <summary>Gets a value indicating whether the patch delivered commits that wait on this node to be applied.</summary>
    public bool CanApplyCommits => IsCommits && Patch.Direction == PatchDirection.Incoming && Patch.Status == PatchStatus.Waiting;

    /// <summary>Gets the status label.</summary>
    public string StatusText => DisplayText.Patch(_localizer, Patch.Status);

    /// <summary>Gets the error of the patch worded in the language in use, or <see langword="null"/>.</summary>
    public string? ErrorText => LocalizedText.Render(Patch.Error, _localizer);

    /// <summary>Gets a value indicating whether the patch succeeded.</summary>
    public bool IsSuccess => Patch.Status is PatchStatus.Applied or PatchStatus.ConflictResolved;

    /// <summary>Gets a value indicating whether the patch needs attention.</summary>
    public bool IsProblem => Patch.Status is PatchStatus.ConflictPending or PatchStatus.Failed or PatchStatus.Waiting;

    /// <summary>
    /// Tells the bindings that the texts of the row changed with the language.
    /// </summary>
    public void RefreshTexts() => OnPropertyChanged(string.Empty);

    /// <summary>Gets a value indicating whether the patch can be resolved on this node.</summary>
    public bool CanResolve => !IsCommits && Patch.Direction == PatchDirection.Incoming && Patch.Status is PatchStatus.ConflictPending or PatchStatus.Failed;
}
