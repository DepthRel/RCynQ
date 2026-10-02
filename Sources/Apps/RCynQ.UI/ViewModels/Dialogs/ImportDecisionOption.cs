using RCynQ.Core.Enums;

namespace RCynQ.UI.ViewModels.Dialogs;

/// <summary>
/// One decision of an import, as the dialog offers it.
/// </summary>
/// <param name="Decision">The decision.</param>
/// <param name="Title">The caption.</param>
/// <param name="Hint">What the decision does.</param>
public sealed record ImportDecisionOption(ImportDecision Decision, string Title, string Hint);
