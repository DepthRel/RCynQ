namespace RCynQ.Core.Api;

/// <summary>
/// The body of <c>PUT /changes/partial/content</c>: the version of a file that goes to the peer, as the user left it in
/// an external tool.
/// </summary>
/// <param name="Path">The path relative to the working tree.</param>
/// <param name="BaseSha256">The SHA-256 of the version of the peer the files were prepared for.</param>
/// <param name="WorkdirSha256">The SHA-256 of the working file the files were prepared for.</param>
/// <param name="Content">The version that goes.</param>
public sealed record SetPartialContentRequest(string Path, string? BaseSha256, string? WorkdirSha256, byte[] Content);
