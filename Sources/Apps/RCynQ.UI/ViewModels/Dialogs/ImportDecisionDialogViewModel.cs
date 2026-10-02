using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RCynQ.Core.Api;
using RCynQ.Core.Enums;
using RCynQ.Localization;

namespace RCynQ.UI.ViewModels.Dialogs;

/// <summary>
/// Asks what to do with an imported node when a node of the same role is known already.
/// </summary>
public sealed partial class ImportDecisionDialogViewModel : DialogViewModelBase
{
    /// <summary>The chosen decision.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCandidateVisible))]
    private ImportDecisionOption _selectedOption;

    /// <summary>The link whose node is replaced.</summary>
    [ObservableProperty]
    private ImportCandidateOption? _selectedCandidate;

    /// <summary>What the import asks.</summary>
    private readonly ImportDecisionRequest _request;

    /// <summary>
    /// Initializes a new instance of the <see cref="ImportDecisionDialogViewModel"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the texts.</param>
    /// <param name="result">The answer of the import that waits for a decision.</param>
    /// <exception cref="ArgumentException">The import does not wait for a decision.</exception>
    public ImportDecisionDialogViewModel(ILocalizer localizer, ImportResult result)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(result);
        _request = result.Decision ?? throw new ArgumentException("The import does not wait for a decision.", nameof(result));
        Message = _request.SameNode
            ? localizer.Format("ImportDecision.SameNode", result.PeerId.Value, _request.Host)
            : localizer.Format("ImportDecision.OtherNode", result.PeerId.Value, result.PeerRole);
        Options = _request.Options
            .Select(decision => new ImportDecisionOption(
                decision,
                localizer.Get($"ImportDecision.{decision}"),
                localizer.Get($"ImportDecision.{decision}.Hint")))
            .ToArray();
        Candidates = _request.Candidates
            .Select(candidate => new ImportCandidateOption(candidate.LinkId, $"{candidate.PeerId.Value} ({candidate.Host})"))
            .ToArray();
        _selectedOption = Options[0];
        _selectedCandidate = Candidates.Count > 0 ? Candidates[0] : null;
    }

    /// <summary>Gets the question.</summary>
    public string Message { get; }

    /// <summary>Gets the decisions to choose from.</summary>
    public IReadOnlyList<ImportDecisionOption> Options { get; }

    /// <summary>Gets the nodes one of which the imported node can replace.</summary>
    public IReadOnlyList<ImportCandidateOption> Candidates { get; }

    /// <summary>
    /// Gets a value indicating whether the node to replace must be chosen: another node takes the place of one of
    /// several known ones.
    /// </summary>
    public bool IsCandidateVisible => SelectedOption.Decision == ImportDecision.Replace && !_request.SameNode && Candidates.Count > 1;

    /// <summary>Gets the chosen decision.</summary>
    public ImportDecision Decision => SelectedOption.Decision;

    /// <summary>
    /// Gets the link whose node is replaced, or <see langword="null"/> when the decision names none.
    /// </summary>
    public Guid? LinkId => SelectedOption.Decision == ImportDecision.Replace && !_request.SameNode ? SelectedCandidate?.LinkId : null;

    /// <summary>
    /// Confirms the decision.
    /// </summary>
    [RelayCommand]
    private void Confirm() => Close(true);

    /// <summary>
    /// Cancels the import.
    /// </summary>
    [RelayCommand]
    private void Cancel() => Close(false);
}
