namespace RCynQ.Sync.GitChannel;

/// <summary>
/// A bundle downloaded from Dest.
/// </summary>
/// <param name="Path">The bundle file; the caller deletes it.</param>
/// <param name="Head">The branch Dest has checked out, or <see langword="null"/> for a detached HEAD.</param>
public sealed record GitBundleDownload(string Path, string? Head);
