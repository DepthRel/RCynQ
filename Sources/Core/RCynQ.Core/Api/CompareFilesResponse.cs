using RCynQ.Core.Enums;

namespace RCynQ.Core.Api;

/// <summary>
/// Answer of <c>POST /changes/compare</c>: the files to hand to an external tool.
/// </summary>
/// <param name="Path">The path relative to the working tree.</param>
/// <param name="Availability">Whether the versions are text.</param>
/// <param name="BaseFile">The full path of a read-only copy of the version of the peer (empty when it has none).</param>
/// <param name="WorkFile">
/// The full path of the right side: the working file itself, a read-only empty file when it is deleted, or with
/// <see cref="CompareFilesRequest.ForChoice"/> an editable copy of the version that goes.
/// </param>
/// <param name="BaseSha256">The SHA-256 of the version of the peer, or <see langword="null"/>; passed back with a choice.</param>
/// <param name="WorkdirSha256">The SHA-256 of the working file, or <see langword="null"/>; passed back with a choice.</param>
public sealed record CompareFilesResponse(
    string Path,
    DiffAvailability Availability,
    string BaseFile,
    string WorkFile,
    string? BaseSha256,
    string? WorkdirSha256);
