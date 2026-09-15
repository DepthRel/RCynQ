using Microsoft.AspNetCore.Http;
using RCynQ.Core.Enums;

namespace RCynQ.Service.Endpoints;

/// <summary>
/// Maps patch rejection reasons to HTTP status codes.
/// </summary>
public static class PatchRejectionStatusCodes
{
    /// <summary>
    /// Gets the status code of a rejection reason.
    /// </summary>
    /// <param name="reason">The reason.</param>
    /// <returns>The status code.</returns>
    public static int For(PatchRejectionReason reason) => reason switch
    {
        PatchRejectionReason.Malformed => StatusCodes.Status400BadRequest,
        PatchRejectionReason.WrongReceiver => StatusCodes.Status400BadRequest,
        PatchRejectionReason.InvalidToken => StatusCodes.Status401Unauthorized,
        PatchRejectionReason.UnknownSender => StatusCodes.Status403Forbidden,
        PatchRejectionReason.SenderMismatch => StatusCodes.Status403Forbidden,
        PatchRejectionReason.InvalidSignature => StatusCodes.Status403Forbidden,
        PatchRejectionReason.Replay => StatusCodes.Status409Conflict,
        PatchRejectionReason.Expired => StatusCodes.Status409Conflict,
        PatchRejectionReason.RoleNotAllowed => StatusCodes.Status404NotFound,
        _ => StatusCodes.Status400BadRequest
    };
}
