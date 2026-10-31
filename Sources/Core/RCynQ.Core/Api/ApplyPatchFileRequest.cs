namespace RCynQ.Core.Api;

/// <summary>
/// The body of <c>POST /patches/file/apply</c>.
/// </summary>
/// <param name="Content">The bytes of the patch file; the client reads the file, so no path of the client reaches the service.</param>
/// <param name="Paths">The files of the patch to use, or <see langword="null"/> for all of them.</param>
/// <param name="Reverse">Whether the changes are taken out of the working tree instead of brought in.</param>
public sealed record ApplyPatchFileRequest(byte[] Content, IReadOnlyList<string>? Paths, bool Reverse);
