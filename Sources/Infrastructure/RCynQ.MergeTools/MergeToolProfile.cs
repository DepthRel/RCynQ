using RCynQ.MergeTools.Enums;

namespace RCynQ.MergeTools;

/// <summary>
/// What RCynQ knows about one merge tool: where to look for it and how to hand it the files of a merge.
/// </summary>
/// <param name="Id">The identifier of the tool, as Git names it where Git knows the tool.</param>
/// <param name="Name">The name of the product, shown when the installation has no name of its own.</param>
/// <param name="Discovery">How its installations are found.</param>
/// <param name="Commands">The names of its command looked up in <c>PATH</c> on Linux and macOS.</param>
/// <param name="Candidates">The usual places of its executable.</param>
/// <param name="Arguments">The template of its arguments (see <see cref="MergeToolArguments"/>).</param>
/// <param name="TrustExitCode">
/// Whether a non-zero exit code means that the user gave up the merge, as Git's <c>mergetool.&lt;tool&gt;.trustExitCode</c>.
/// </param>
/// <param name="GitNames">The names under which <c>git config merge.tool</c> chooses the tool.</param>
public sealed record MergeToolProfile(
    string Id,
    string Name,
    MergeToolDiscovery Discovery,
    IReadOnlyList<string> Commands,
    IReadOnlyList<MergeToolCandidate> Candidates,
    IReadOnlyList<string> Arguments,
    bool TrustExitCode,
    IReadOnlyList<string> GitNames);
