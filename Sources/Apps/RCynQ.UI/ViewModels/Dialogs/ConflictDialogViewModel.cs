using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RCynQ.Core.Enums;
using RCynQ.Core.Patches;
using RCynQ.Localization;
using RCynQ.MergeTools;
using RCynQ.UI.ViewModels.Items;

namespace RCynQ.UI.ViewModels.Dialogs;

/// <summary>
/// The conflict dialog: shows the conflicting files and lets the user pick a policy or a merge tool.
/// </summary>
public sealed partial class ConflictDialogViewModel : DialogViewModelBase
{
    /// <summary>The localizer of the messages.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>The merge tool the files are merged in when the user picks the external merge.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MergeToolText))]
    [NotifyPropertyChangedFor(nameof(HasMergeTool))]
    [NotifyCanExecuteChangedFor(nameof(UseMergeToolCommand))]
    private MergeTool? _selectedTool;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConflictDialogViewModel"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="patchId">The patch identifier.</param>
    /// <param name="sender">The sender node.</param>
    /// <param name="conflicts">The conflicting files.</param>
    /// <param name="tools">The merge tools of the machine, or <see langword="null"/> when none are offered.</param>
    /// <param name="defaultTool">The merge tool chosen in the settings, or <see langword="null"/>.</param>
    public ConflictDialogViewModel(
        ILocalizer localizer,
        Guid patchId,
        string sender,
        IReadOnlyList<ConflictFile> conflicts,
        IReadOnlyList<MergeTool>? tools = null,
        MergeTool? defaultTool = null)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        PatchId = patchId;
        Sender = sender;
        ArgumentNullException.ThrowIfNull(conflicts);
        Files = [.. conflicts.Select(file => new ConflictFileItemViewModel(_localizer, file))];

        // The tool of the settings comes first; a tool the user described is not among the found ones, so it is added.
        var offered = (tools ?? Array.Empty<MergeTool>()).ToList();
        if (defaultTool is not null && !offered.Contains(defaultTool))
        {
            offered.Insert(0, defaultTool);
        }

        Tools = offered;
        _selectedTool = defaultTool ?? offered.FirstOrDefault();
    }

    /// <summary>Gets the patch identifier.</summary>
    public Guid PatchId { get; }

    /// <summary>Gets the sender node.</summary>
    public string Sender { get; }

    /// <summary>Gets the conflicting files.</summary>
    public IReadOnlyList<ConflictFileItemViewModel> Files { get; }

    /// <summary>Gets the merge tools the user can pick from.</summary>
    public IReadOnlyList<MergeTool> Tools { get; }

    /// <summary>Gets a value indicating whether there are several merge tools to pick from.</summary>
    public bool HasToolChoice => Tools.Count > 1;

    /// <summary>Gets a value indicating whether a merge tool is there to merge the files in.</summary>
    public bool HasMergeTool => SelectedTool is not null;

    /// <summary>Gets the title of the choice that merges the files in the tool.</summary>
    public string MergeToolText => SelectedTool is { } tool
        ? _localizer.Format("Conflict.ExternalTool", tool.Name)
        : _localizer.Get("Conflict.ExternalToolNone");

    /// <summary>Gets the header text.</summary>
    public string Header => Files.Count == 0
        ? _localizer.Format("Conflict.HeaderNoFiles", Sender)
        : _localizer.Format("Conflict.Header", Sender, Files.Count);

    /// <summary>Gets the choice, or <see langword="null"/> when cancelled.</summary>
    public ConflictChoice? Result { get; private set; }

    /// <summary>
    /// Keeps the local state.
    /// </summary>
    [RelayCommand]
    private void KeepLocal() => Choose(new ConflictChoice(ConflictPolicy.KeepLocal, null));

    /// <summary>
    /// Applies the incoming files over the local state (conflicting files are backed up).
    /// </summary>
    [RelayCommand]
    private void ApplyIncoming() => Choose(new ConflictChoice(ConflictPolicy.ApplyIncomingOverLocal, null));

    /// <summary>
    /// Merges the incoming files into the local ones with Git.
    /// </summary>
    [RelayCommand]
    private void UseGit() => Choose(new ConflictChoice(ConflictPolicy.UseGit, null));

    /// <summary>
    /// Merges the files in the selected merge tool.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasMergeTool))]
    private void UseMergeTool()
    {
        if (SelectedTool is { } tool)
        {
            Choose(new ConflictChoice(null, tool));
        }
    }

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
    /// Records a choice and closes the dialog.
    /// </summary>
    /// <param name="choice">The choice.</param>
    private void Choose(ConflictChoice choice)
    {
        Result = choice;
        Close(true);
    }
}
