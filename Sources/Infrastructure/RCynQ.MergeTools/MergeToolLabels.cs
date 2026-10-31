namespace RCynQ.MergeTools;

/// <summary>
/// The titles of the panes of a merge tool, in the language of the user.
/// </summary>
/// <param name="Local">The title of the local version.</param>
/// <param name="Base">The title of the common ancestor.</param>
/// <param name="Incoming">The title of the incoming version.</param>
/// <param name="Result">The title of the result.</param>
public sealed record MergeToolLabels(string Local, string Base, string Incoming, string Result);
