namespace RCynQ.Core.Api;

/// <summary>
/// One file of a merge session that waits for the user in a merge tool.
/// </summary>
/// <param name="Path">The path of the file in the repository.</param>
/// <param name="LocalFile">The full path of the copy of the local version.</param>
/// <param name="BaseFile">The full path of the copy of the common ancestor.</param>
/// <param name="IncomingFile">The full path of the copy of the incoming version.</param>
/// <param name="ResultFile">The full path of the file the tool writes the merge to; it starts with Git's merge and its conflict markers.</param>
public sealed record MergeSessionFile(string Path, string LocalFile, string BaseFile, string IncomingFile, string ResultFile);
