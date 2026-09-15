using System.Text;
using System.Text.RegularExpressions;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;

namespace RCynQ.Ssh.Keys.AuthorizedKeys;

/// <summary>
/// Defines what a key added to <c>authorized_keys</c> may do.
/// </summary>
public enum AuthorizedKeyPurpose
{
    /// <summary>
    /// Read-only Git access on Dest: forced <c>git-upload-pack</c> for one repository, no push.
    /// </summary>
    GitReadOnly = 1,

    /// <summary>
    /// Port forwarding to the local patch endpoint only.
    /// </summary>
    PatchForward = 2,

    /// <summary>
    /// Mediator jump access: port forwarding to the next hop only.
    /// </summary>
    MediatorForward = 3
}
