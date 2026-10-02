namespace RCynQ.Core.Enums;

/// <summary>
/// Defines what a key added to <c>authorized_keys</c> may do.
/// </summary>
public enum AuthorizedKeyPurpose
{
    /// <summary>
    /// Read-only Git access on Origin: forced <c>git-upload-pack</c> for one repository, no push.
    /// </summary>
    GitReadOnly = 1,

    /// <summary>
    /// Port forwarding to the local patch endpoint only.
    /// </summary>
    PatchForward = 2,

    /// <summary>
    /// Relay jump access: port forwarding to the next hop only.
    /// </summary>
    RelayForward = 3
}
