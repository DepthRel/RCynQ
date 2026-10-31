namespace RCynQ.Core.Api;

/// <summary>
/// The body of <c>POST /patches/file</c>.
/// </summary>
/// <param name="Paths">
/// The relative paths of the files to save, or <see langword="null"/> to save every changed file. Paths that are not among the
/// changed files are ignored.
/// </param>
public sealed record BuildPatchFileRequest(IReadOnlyList<string>? Paths);
