using System.Text.RegularExpressions;
using RCynQ.Core.Enums;

namespace RCynQ.Git.Patches.Files;

/// <summary>
/// Tells from the errors of <c>git apply</c>, printed in English, what went wrong and with which files.
/// </summary>
internal static partial class GitApplyErrorReader
{
    /// <summary>
    /// Reads the errors of <c>git apply</c>.
    /// </summary>
    /// <param name="errors">The standard error of the command.</param>
    /// <returns>The first failure in the order of importance, and the files Git names for it.</returns>
    public static (PatchFileFailure Failure, IReadOnlyList<string> Files) Read(string errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        var failures = new (PatchFileFailure Failure, Regex Pattern)[]
        {
            (PatchFileFailure.UnsafePath, UnsafePath()),
            (PatchFileFailure.NotAPatch, NotAPatch()),
            (PatchFileFailure.AlreadyExists, AlreadyExists()),
            (PatchFileFailure.MissingFile, MissingFile()),
            (PatchFileFailure.DoesNotApply, DoesNotApply())
        };

        foreach (var (failure, pattern) in failures)
        {
            var matches = pattern.Matches(errors);
            if (matches.Count > 0)
            {
                return (failure, matches.Select(match => match.Groups["path"].Value).Where(path => path.Length > 0).Distinct(StringComparer.Ordinal).ToArray());
            }
        }

        return (PatchFileFailure.Other, Array.Empty<string>());
    }

    /// <summary>Matches the refusal of a path outside the working tree, inside <c>.git</c> or behind a symbolic link.</summary>
    /// <returns>The pattern.</returns>
    [GeneratedRegex(@"^error: (?:invalid path|affected file) '(?<path>.+?)'", RegexOptions.Multiline)]
    private static partial Regex UnsafePath();

    /// <summary>Matches the errors about input that is not a patch.</summary>
    /// <returns>The pattern.</returns>
    [GeneratedRegex(@"^(?:error|fatal): (?:No valid patches in input|corrupt patch at|unrecognized input|git diff header lacks|git apply: bad git-diff|cannot parse)", RegexOptions.Multiline)]
    private static partial Regex NotAPatch();

    /// <summary>Matches a new file that exists already.</summary>
    /// <returns>The pattern.</returns>
    [GeneratedRegex(@"^error: (?<path>.+?): already exists in working directory", RegexOptions.Multiline)]
    private static partial Regex AlreadyExists();

    /// <summary>Matches a changed file that does not exist.</summary>
    /// <returns>The pattern.</returns>
    [GeneratedRegex(@"^error: (?<path>.+?): No such file or directory", RegexOptions.Multiline)]
    private static partial Regex MissingFile();

    /// <summary>Matches a file whose content does not fit the patch.</summary>
    /// <returns>The pattern.</returns>
    [GeneratedRegex(@"^error: (?:(?<path>.+?): (?:patch does not apply|wrong type)|(?:binary patch does not apply to|cannot apply binary patch to|the patch applies to) '(?<path>.+?)')", RegexOptions.Multiline)]
    private static partial Regex DoesNotApply();
}
