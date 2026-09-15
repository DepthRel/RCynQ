namespace RCynQ.Core.Api;

/// <summary>
/// The error body of failed API requests (except patch rejections, which use <c>PatchRejectionResponse</c>).
/// </summary>
/// <param name="Error">A human-readable explanation.</param>
public sealed record ErrorResponse(string Error);
