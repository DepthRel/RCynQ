namespace RCynQ.Core.Api;

/// <summary>
/// The body of <c>POST /patches/file/read</c>.
/// </summary>
/// <param name="Content">The bytes of the patch file; the client reads the file, so no path of the client reaches the service.</param>
public sealed record ReadPatchFileRequest(byte[] Content);
