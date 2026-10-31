namespace RCynQ.Git.Patches.Files;

/// <summary>
/// What to apply of a patch file, and in which direction.
/// </summary>
/// <param name="PatchFile">The full path of the patch file.</param>
/// <param name="Reverse">Whether the changes of the patch are taken out of the working tree instead of brought in.</param>
/// <param name="Chosen">The files of the patch to apply, relative to the working tree.</param>
/// <param name="Skipped">The files of the patch to leave out, relative to the working tree; none applies the whole patch.</param>
public sealed record GitPatchApplyRequest(string PatchFile, bool Reverse, IReadOnlyCollection<string> Chosen, IReadOnlyCollection<string> Skipped);
