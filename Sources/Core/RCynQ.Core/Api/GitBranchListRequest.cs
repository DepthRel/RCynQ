namespace RCynQ.Core.Api;

/// <summary>
/// The request of a Workstation for the list of branches of a repository of Origin.
/// </summary>
/// <param name="Repository">
/// The name of the repository asked for, one of those the answering node registered; <see langword="null"/> asks for the
/// repository that node works with.
/// </param>
public sealed record GitBranchListRequest(string? Repository = null);
