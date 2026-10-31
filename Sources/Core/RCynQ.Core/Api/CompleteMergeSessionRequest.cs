namespace RCynQ.Core.Api;

/// <summary>
/// Body of <c>POST /merge-sessions/{id}/complete</c>.
/// </summary>
/// <param name="ToolName">The name of the merge tool the files were merged in, recorded in the history.</param>
public sealed record CompleteMergeSessionRequest(string ToolName);
