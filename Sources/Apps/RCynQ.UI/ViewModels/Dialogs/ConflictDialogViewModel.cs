using CommunityToolkit.Mvvm.Input;
using RCynQ.Core.Enums;
using RCynQ.Core.Patches;
using RCynQ.Localization;
using RCynQ.UI.ViewModels.Items;

namespace RCynQ.UI.ViewModels.Dialogs;

/// <summary>
/// The conflict dialog: shows the conflicting files and lets the user pick a policy.
/// </summary>
public sealed partial class ConflictDialogViewModel : DialogViewModelBase
{
    /// <summary>The localizer of the messages.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConflictDialogViewModel"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="patchId">The patch identifier.</param>
    /// <param name="sender">The sender node.</param>
    /// <param name="conflicts">The conflicting files.</param>
    public ConflictDialogViewModel(ILocalizer localizer, Guid patchId, string sender, IReadOnlyList<ConflictFile> conflicts)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        PatchId = patchId;
        Sender = sender;
        ArgumentNullException.ThrowIfNull(conflicts);
        Files = [.. conflicts.Select(file => new ConflictFileItemViewModel(_localizer, file))];
    }

    /// <summary>Gets the patch identifier.</summary>
    public Guid PatchId { get; }

    /// <summary>Gets the sender node.</summary>
    public string Sender { get; }

    /// <summary>Gets the conflicting files.</summary>
    public IReadOnlyList<ConflictFileItemViewModel> Files { get; }

    /// <summary>Gets the header text.</summary>
    public string Header => Files.Count == 0
        ? _localizer.Format("Conflict.HeaderNoFiles", Sender)
        : _localizer.Format("Conflict.Header", Sender, Files.Count);

    /// <summary>Gets the chosen policy, or <see langword="null"/> when cancelled.</summary>
    public ConflictPolicy? Result { get; private set; }

    /// <summary>
    /// Keeps the local state.
    /// </summary>
    [RelayCommand]
    private void KeepLocal() => Choose(ConflictPolicy.KeepLocal);

    /// <summary>
    /// Applies the incoming files over the local state (conflicting files are backed up).
    /// </summary>
    [RelayCommand]
    private void ApplyIncoming() => Choose(ConflictPolicy.ApplyIncomingOverLocal);

    /// <summary>
    /// Merges the incoming files into the local ones with Git.
    /// </summary>
    [RelayCommand]
    private void UseGit() => Choose(ConflictPolicy.UseGit);

    /// <summary>
    /// Closes the dialog without a decision.
    /// </summary>
    [RelayCommand]
    private void Cancel()
    {
        Result = null;
        Close(false);
    }

    /// <summary>
    /// Records a policy and closes the dialog.
    /// </summary>
    /// <param name="policy">The policy.</param>
    private void Choose(ConflictPolicy policy)
    {
        Result = policy;
        Close(true);
    }
}
