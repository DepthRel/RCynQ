using System.ComponentModel;
using System.Text.Json.Serialization;

namespace RCynQ.Core.Enums;

/// <summary>
/// Defines why a received patch envelope was refused before it was stored.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<PatchRejectionReason>))]
public enum PatchRejectionReason
{
    /// <summary>
    /// The envelope or its payload is malformed.
    /// </summary>
    [Description("Envelope is malformed.")]
    Malformed = 1,

    /// <summary>
    /// The link token is missing or does not match.
    /// </summary>
    [Description("Link token is missing or invalid.")]
    InvalidToken = 2,

    /// <summary>
    /// The sender is not a known peer or has no signing key configured.
    /// </summary>
    [Description("Sender is not a known peer.")]
    UnknownSender = 3,

    /// <summary>
    /// The sender header does not match the envelope sender.
    /// </summary>
    [Description("Sender header does not match the envelope.")]
    SenderMismatch = 4,

    /// <summary>
    /// The envelope is addressed to a different node.
    /// </summary>
    [Description("Envelope is addressed to a different node.")]
    WrongReceiver = 5,

    /// <summary>
    /// The envelope signature is missing or invalid.
    /// </summary>
    [Description("Signature is missing or invalid.")]
    InvalidSignature = 6,

    /// <summary>
    /// An envelope with the same identifier was already processed.
    /// </summary>
    [Description("Envelope was already processed.")]
    Replay = 7,

    /// <summary>
    /// The envelope creation time is outside the accepted window.
    /// </summary>
    [Description("Envelope is too old or from the future.")]
    Expired = 8,

    /// <summary>
    /// The local node role does not accept patches.
    /// </summary>
    [Description("Local role does not accept patches.")]
    RoleNotAllowed = 9,

    /// <summary>
    /// The patch belongs to a repository the receiving node does not work with now.
    /// </summary>
    [Description("The receiving node works with another repository.")]
    RepositoryMismatch = 10,

    /// <summary>
    /// The request names a repository the answering node has not registered.
    /// </summary>
    [Description("The node has no repository of that name.")]
    UnknownRepository = 11,

    /// <summary>
    /// The receiving Origin does not accept commits from the Workstation.
    /// </summary>
    [Description("The node does not accept commits.")]
    CommitsNotAccepted = 12,

    /// <summary>
    /// The commits are not built on the commit the receiving node has on top of the same checked out branch.
    /// </summary>
    [Description("The branch or its last commit differs from the one the commits are built on.")]
    VersionMismatch = 13,

    /// <summary>
    /// Commits of an earlier delivery are waiting to be applied on the receiving node.
    /// </summary>
    [Description("Commits of an earlier delivery are waiting to be applied.")]
    CommitsWaiting = 14
}
