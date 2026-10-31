namespace RCynQ.MergeTools;

/// <summary>
/// A merge tool that can be started: one installation of a known tool, or the tool the user described.
/// </summary>
/// <param name="ProfileId">The identifier of the profile of the tool, or <see cref="CUSTOM_ID"/>.</param>
/// <param name="Name">The name shown to the user.</param>
/// <param name="ExecutablePath">The full path of the executable.</param>
/// <param name="Arguments">The template of its arguments (see <see cref="MergeToolArguments"/>).</param>
/// <param name="TrustExitCode">Whether a non-zero exit code means that the user gave up the merge.</param>
public sealed record MergeTool(string ProfileId, string Name, string ExecutablePath, IReadOnlyList<string> Arguments, bool TrustExitCode)
{
    /// <summary>The profile identifier of the tool the user described: an executable and a template of its arguments.</summary>
    public const string CUSTOM_ID = "custom";

    /// <summary>
    /// Gets a value indicating whether this is the tool the user described.
    /// </summary>
    public bool IsCustom => ProfileId == CUSTOM_ID;
}
