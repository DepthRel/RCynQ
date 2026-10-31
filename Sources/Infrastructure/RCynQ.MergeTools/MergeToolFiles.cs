namespace RCynQ.MergeTools;

/// <summary>
/// The files of one three-way merge handed to a tool.
/// </summary>
/// <param name="Local">The local version of the file.</param>
/// <param name="Base">The common ancestor: the version both sides started from.</param>
/// <param name="Incoming">The incoming version of the file.</param>
/// <param name="Result">The file the tool writes the merge to.</param>
public sealed record MergeToolFiles(string Local, string Base, string Incoming, string Result);
