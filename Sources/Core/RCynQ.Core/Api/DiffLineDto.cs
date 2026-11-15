using RCynQ.Core.Enums;

namespace RCynQ.Core.Api;

/// <summary>
/// One line of the diff of a file.
/// </summary>
/// <param name="Kind">Whether the line is around a change, only on the peer or only in the working tree.</param>
/// <param name="BaseNumber">The number of the line in the version of the peer (from 1), or <see langword="null"/> for an added line.</param>
/// <param name="WorkNumber">The number of the line in the working file (from 1), or <see langword="null"/> for a removed line.</param>
/// <param name="Text">The text of the line without its line ending.</param>
public sealed record DiffLineDto(DiffLineKind Kind, int? BaseNumber, int? WorkNumber, string Text);
