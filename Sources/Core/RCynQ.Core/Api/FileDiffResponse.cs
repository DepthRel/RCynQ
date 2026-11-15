using RCynQ.Core.Enums;

namespace RCynQ.Core.Api;

/// <summary>
/// Answer of <c>GET /changes/diff</c>: what the working file changes against the version of the peer.
/// </summary>
/// <param name="Path">The path relative to the working tree.</param>
/// <param name="Availability">Whether the diff is shown line by line.</param>
/// <param name="Partial">Whether only a part of the changes goes now.</param>
/// <param name="CanChoose">Whether a part of the changes can be chosen: a text file the peer does not have yet, that is not deleted.</param>
/// <param name="CustomChoice">
/// Whether the current choice is not made of whole changes (it was edited in an external tool, or the file changed
/// since); choosing here replaces it.
/// </param>
/// <param name="BaseSha256">The SHA-256 of the version of the peer, or <see langword="null"/> when it has none; passed back with a choice.</param>
/// <param name="WorkdirSha256">The SHA-256 of the working file, or <see langword="null"/> when it is deleted; passed back with a choice.</param>
/// <param name="Blocks">The changes, in the order of the file; empty when the diff is not shown line by line.</param>
public sealed record FileDiffResponse(
    string Path,
    DiffAvailability Availability,
    PartialChangeState Partial,
    bool CanChoose,
    bool CustomChoice,
    string? BaseSha256,
    string? WorkdirSha256,
    IReadOnlyList<DiffBlockDto> Blocks);
