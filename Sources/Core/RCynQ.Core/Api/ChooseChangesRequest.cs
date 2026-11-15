namespace RCynQ.Core.Api;

/// <summary>
/// The body of <c>PUT /changes/partial</c>: the changes of a file that go to the peer.
/// </summary>
/// <param name="Path">The path relative to the working tree.</param>
/// <param name="BaseSha256">The SHA-256 of the version of the peer the diff was shown for.</param>
/// <param name="WorkdirSha256">The SHA-256 of the working file the diff was shown for.</param>
/// <param name="Blocks">The indexes of the chosen changes (<see cref="DiffBlockDto.Index"/>).</param>
public sealed record ChooseChangesRequest(string Path, string? BaseSha256, string? WorkdirSha256, IReadOnlyList<int> Blocks);
