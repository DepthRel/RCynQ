using CommunityToolkit.Mvvm.Input;
using RCynQ.Core.Enums;
using RCynQ.Core.Patches;

namespace RCynQ.UI.ViewModels.Dialogs;

/// <summary>
/// The conflict dialog: shows the conflicting files and lets the user pick a policy.
/// </summary>
public sealed partial class ConflictDialogViewModel : DialogViewModelBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ConflictDialogViewModel"/> class.
    /// </summary>
    /// <param name="patchId">The patch identifier.</param>
    /// <param name="sender">The sender node.</param>
    /// <param name="conflicts">The conflicting files.</param>
    public ConflictDialogViewModel(Guid patchId, string sender, IReadOnlyList<ConflictFile> conflicts)
    {
        PatchId = patchId;
        Sender = sender;
        Files = conflicts ?? throw new ArgumentNullException(nameof(conflicts));
    }

    /// <summary>Gets the patch identifier.</summary>
    public Guid PatchId { get; }

    /// <summary>Gets the sender node.</summary>
    public string Sender { get; }

    /// <summary>Gets the conflicting files.</summary>
    public IReadOnlyList<ConflictFile> Files { get; }

    /// <summary>Gets the header text.</summary>
    public string Header => Files.Count == 0
        ? $"Патч от {Sender} не удалось применить. Выберите, как поступить."
        : $"Патч от {Sender} конфликтует с локальными изменениями в {Files.Count} файл(ах).";

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
    /// Uses Git tools (<c>--3way</c> / <c>--reject</c>).
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
