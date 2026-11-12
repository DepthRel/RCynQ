using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RCynQ.Core.Config;
using RCynQ.Localization;

namespace RCynQ.UI.ViewModels.Dialogs;

/// <summary>
/// The confirmation of a reset of the node: it says what goes and what stays, and lets the reset start only once the user
/// typed the identifier of the node.
/// </summary>
public sealed partial class ResetNodeDialogViewModel : DialogViewModelBase
{
    /// <summary>The localizer of the texts.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>The plan of the reset.</summary>
    private readonly NodeResetPlan _plan;

    /// <summary>What the user typed to confirm.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirm))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _typed = string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="ResetNodeDialogViewModel"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the texts.</param>
    /// <param name="plan">The plan of the reset.</param>
    public ResetNodeDialogViewModel(ILocalizer localizer, NodeResetPlan plan)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _plan = plan ?? throw new ArgumentNullException(nameof(plan));
    }

    /// <summary>Gets the identifier of the node, the word that confirms the reset.</summary>
    public string NodeId => _plan.NodeId;

    /// <summary>Gets the item about the configuration file that goes.</summary>
    public string ConfigText => _localizer.Get("Reset.Config");

    /// <summary>Gets the path of the configuration file, shown under its item.</summary>
    public string ConfigPath => _plan.ConfigPath;

    /// <summary>Gets the item about the data directory: what goes from it, or that it holds no data of a node.</summary>
    public string DataText => _localizer.Get(_plan.HasData ? "Reset.Data" : "Reset.NoData");

    /// <summary>Gets the path of the data directory, shown under its item.</summary>
    public string DataPath => _plan.DataRoot;

    /// <summary>Gets the line asking to type the identifier of the node.</summary>
    public string PromptText => _localizer.Format("Reset.Dialog.Prompt", _plan.NodeId);

    /// <summary>Gets a value indicating whether the user typed the identifier of the node.</summary>
    public bool CanConfirm => string.Equals(Typed.Trim(), _plan.NodeId, StringComparison.Ordinal);

    /// <summary>
    /// Starts the reset.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm() => Close(true);

    /// <summary>
    /// Leaves the node as it is.
    /// </summary>
    [RelayCommand]
    private void Cancel() => Close(false);
}
