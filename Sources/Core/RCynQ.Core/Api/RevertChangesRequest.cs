namespace RCynQ.Core.Api;

/// <summary>
/// Request to take back the changes of one file of the working tree that are not staged.
/// </summary>
/// <param name="Path">The path relative to the working tree.</param>
public sealed record RevertChangesRequest(string Path);
