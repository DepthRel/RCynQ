namespace RCynQ.Core.Api;

/// <summary>
/// How joining a node with an invitation ended.
/// </summary>
/// <param name="Joined">Whether every stage succeeded.</param>
/// <param name="Message">The localized summary.</param>
/// <param name="Steps">The stages that ran, in order; the last one failed when <paramref name="Joined"/> is false.</param>
public sealed record JoinResult(bool Joined, string Message, IReadOnlyList<JoinStepResult> Steps);
