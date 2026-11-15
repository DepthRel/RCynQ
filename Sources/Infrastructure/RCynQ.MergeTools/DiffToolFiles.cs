namespace RCynQ.MergeTools;

/// <summary>
/// The files of one diff of two files handed to a tool.
/// </summary>
/// <param name="Left">The file on the left: the version of the other node.</param>
/// <param name="Right">The file on the right: the working file, or the version that goes when a part is chosen.</param>
public sealed record DiffToolFiles(string Left, string Right);
