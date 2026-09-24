namespace RCynQ.Core.Api;

/// <summary>
/// The body of <c>POST /patches/send</c>.
/// </summary>
/// <param name="Paths">
/// The relative paths of the files to send, or <see langword="null"/> to send every changed file. Paths that are not among the
/// changed files are ignored.
/// </param>
public sealed record SendPatchRequest(IReadOnlyList<string>? Paths);
