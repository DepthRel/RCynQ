using RCynQ.Core.Enums;

namespace RCynQ.Git.Patches.Files;

/// <summary>
/// How applying a patch file ended. It either changes every chosen file or none.
/// </summary>
/// <param name="Succeeded">Whether the patch was applied.</param>
/// <param name="Failure">Why it was not, or <see langword="null"/> after a success.</param>
/// <param name="Files">The files that stood in the way, relative to the working tree, as far as Git names them.</param>
/// <param name="Output">What Git printed.</param>
public sealed record GitPatchApplyResult(bool Succeeded, PatchFileFailure? Failure, IReadOnlyList<string> Files, string Output);
