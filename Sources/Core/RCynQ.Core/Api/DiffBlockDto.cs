namespace RCynQ.Core.Api;

/// <summary>
/// One change of a file that can be chosen on its own: lines of the peer replaced by lines of the working file, with the
/// unchanged lines around it.
/// </summary>
/// <param name="Index">The number of the change in the file, from 0.</param>
/// <param name="BaseStart">The first replaced line of the version of the peer, from 0.</param>
/// <param name="BaseCount">How many lines of the version of the peer the change replaces.</param>
/// <param name="WorkStart">The first line of the working file the change brings, from 0.</param>
/// <param name="WorkCount">How many lines of the working file the change brings.</param>
/// <param name="Selected">Whether the change goes to the peer with the current choice.</param>
/// <param name="Lines">The lines to show: the unchanged lines before, the removed and added lines, the unchanged lines after.</param>
public sealed record DiffBlockDto(int Index, int BaseStart, int BaseCount, int WorkStart, int WorkCount, bool Selected, IReadOnlyList<DiffLineDto> Lines);
