namespace RCynQ.UI.ViewModels.Dialogs;

/// <summary>
/// A node an imported node can replace, as the dialog offers it.
/// </summary>
/// <param name="LinkId">The link of the node.</param>
/// <param name="Title">The caption.</param>
public sealed record ImportCandidateOption(Guid LinkId, string Title);
