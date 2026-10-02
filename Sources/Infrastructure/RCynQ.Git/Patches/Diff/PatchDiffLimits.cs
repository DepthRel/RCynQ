namespace RCynQ.Git.Patches.Diff;

/// <summary>
/// Size limits of a diff built for display.
/// </summary>
/// <param name="MaxFileBytes">The largest file (either side) whose content is included, or <see langword="null"/> for no limit.</param>
/// <param name="MaxTotalBytes">The total size of all included files, or <see langword="null"/> for no limit.</param>
public sealed record PatchDiffLimits(long? MaxFileBytes, long? MaxTotalBytes)
{
    /// <summary>
    /// Gets the limits for a diff that is applied with <c>git apply</c>: every file is included.
    /// </summary>
    public static PatchDiffLimits Full { get; } = new(null, null);

    /// <summary>
    /// Gets the limits for a diff that is only shown to the user: large files are listed but not diffed.
    /// </summary>
    public static PatchDiffLimits Display { get; } = new(256 * 1024, 4 * 1024 * 1024);
}
