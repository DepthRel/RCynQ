namespace RCynQ.MergeTools;

/// <summary>
/// The titles of the two panes of a diff, in the language of the user.
/// </summary>
/// <param name="Left">The title of the left pane.</param>
/// <param name="Right">The title of the right pane.</param>
public sealed record DiffToolLabels(string Left, string Right);
