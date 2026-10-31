using RCynQ.MergeTools.Enums;

namespace RCynQ.MergeTools;

/// <summary>
/// A usual place of a merge tool on one operating system.
/// </summary>
/// <remarks>
/// The path is written with <c>/</c> between its parts. It may start with an environment variable written as
/// <c>%NAME%</c> or with <c>~</c> for the home directory, and a directory part may hold <c>*</c> to stand for any
/// folder (the version of an IDE, the folder of an application bundle). The folder a <c>*</c> matched names the
/// installation when there are several of them.
/// </remarks>
/// <param name="Platform">The operating system the place belongs to.</param>
/// <param name="Path">The path of the executable.</param>
public sealed record MergeToolCandidate(MergeToolPlatform Platform, string Path);
