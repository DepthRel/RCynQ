using RCynQ.Core.Enums;

namespace RCynQ.Core.Api;

/// <summary>
/// Tells the caller of an import that a decision is needed, and what can be decided.
/// </summary>
/// <param name="SameNode">
/// Whether the imported configuration describes a node that is already known, at another address.
/// </param>
/// <param name="Host">The imported host.</param>
/// <param name="Options">The decisions that make sense for this import, the suggested one first.</param>
/// <param name="Candidates">
/// The links to peers of the imported role. <see cref="ImportDecision.Replace"/> names one of them, unless the same node
/// is imported, in which case all of its links take the new address.
/// </param>
public sealed record ImportDecisionRequest(
    bool SameNode,
    string Host,
    IReadOnlyList<ImportDecision> Options,
    IReadOnlyList<ImportCandidate> Candidates);
