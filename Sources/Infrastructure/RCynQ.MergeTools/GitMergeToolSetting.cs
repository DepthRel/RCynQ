namespace RCynQ.MergeTools;

/// <summary>
/// The merge tool the user chose for Git.
/// </summary>
/// <param name="Name">The value of <c>merge.tool</c>.</param>
/// <param name="Path">The value of <c>mergetool.&lt;name&gt;.path</c>, or <see langword="null"/> when it is not set.</param>
public sealed record GitMergeToolSetting(string Name, string? Path);
