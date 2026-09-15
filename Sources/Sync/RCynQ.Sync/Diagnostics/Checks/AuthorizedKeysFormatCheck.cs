using System.Text.RegularExpressions;
using RCynQ.Core.Abstractions;
using RCynQ.Core.Config;
using RCynQ.Core.Diagnostics;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.State;
using RCynQ.Git.Commands;
using RCynQ.Ssh.Keys;
using RCynQ.Ssh.Keys.AuthorizedKeys;
using RCynQ.Sync.Security;
using RCynQ.Transports.Abstractions;

namespace RCynQ.Sync.Diagnostics.Checks;

/// <summary>
/// Checks the format of <c>authorized_keys</c> and that every RCynQ-managed key is restricted.
/// </summary>
public sealed class AuthorizedKeysFormatCheck : DiagnosticCheckBase
{
    /// <summary>The authorized keys manager.</summary>
    private readonly AuthorizedKeysManager _keys;

    /// <summary>
    /// Initializes a new instance of the <see cref="AuthorizedKeysFormatCheck"/> class.
    /// </summary>
    /// <param name="keys">The authorized keys manager.</param>
    public AuthorizedKeysFormatCheck(AuthorizedKeysManager keys)
        : base("AuthorizedKeysFormat", AllRoles)
    {
        _keys = keys;
    }

    /// <inheritdoc />
    public override async Task<DiagnosticResult> RunAsync(DiagnosticContext context, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_keys.FilePath))
        {
            return context.Node.Role == NodeRole.Source
                ? Pass($"'{_keys.FilePath}' does not exist yet.")
                : Fail($"'{_keys.FilePath}' does not exist.", "Import the peer keys and add restricted entries for them.");
        }

        AuthorizedKeysFile file;
        try
        {
            file = await _keys.ReadAsync(cancellationToken);
        }
        catch (UnauthorizedAccessException)
        {
            return Pass($"'{_keys.FilePath}' exists; its content can only be checked with administrator rights.");
        }

        var unrestricted = file.Entries
            .Where(entry => entry.Key.Comment?.StartsWith("rcynq:", StringComparison.Ordinal) == true && !entry.IsRestricted)
            .Select(entry => entry.LineNumber)
            .ToArray();

        if (file.InvalidLines.Count == 0 && unrestricted.Length == 0)
        {
            return Pass($"{file.Entries.Count} key(s), all RCynQ keys are restricted.");
        }

        var problems = new List<string>();
        if (file.InvalidLines.Count > 0)
        {
            problems.Add($"unparsable lines: {string.Join(", ", file.InvalidLines)}");
        }

        if (unrestricted.Length > 0)
        {
            problems.Add($"unrestricted RCynQ keys on lines: {string.Join(", ", unrestricted)}");
        }

        return Fail(string.Join("; ", problems), "Remove the listed lines and re-add the keys with restricted options.");
    }
}
