using System.Text;
using System.Text.RegularExpressions;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;

namespace RCynQ.Ssh.Keys.AuthorizedKeys;

/// <summary>
/// A parsed <c>authorized_keys</c> line.
/// </summary>
/// <param name="Options">The options field, or <see langword="null"/>.</param>
/// <param name="Key">The public key.</param>
/// <param name="LineNumber">The 1-based line number.</param>
public sealed record AuthorizedKeyEntry(string? Options, SshPublicKey Key, int LineNumber)
{
    /// <summary>
    /// Gets a value indicating whether the entry is restricted by <c>restrict</c> or a forced command.
    /// </summary>
    public bool IsRestricted => Options is not null
        && (Options.Split(',').Any(option => option.Trim().Equals("restrict", StringComparison.OrdinalIgnoreCase))
            || Options.Contains("command=", StringComparison.OrdinalIgnoreCase));
}
