namespace RCynQ.MergeTools;

/// <summary>
/// The merge tools found on the machine.
/// </summary>
/// <param name="Tools">The tools, in the order of the catalog.</param>
/// <param name="Suggested">
/// The tool offered when the user chose none: the one <c>git config merge.tool</c> names when it was found, otherwise the
/// first one; <see langword="null"/> when nothing was found.
/// </param>
public sealed record MergeToolScan(IReadOnlyList<MergeTool> Tools, MergeTool? Suggested);
