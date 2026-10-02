using RCynQ.Core.Diagnostics;
using RCynQ.Core.Enums;
using RCynQ.Ssh.Keys.AuthorizedKeys;
using RCynQ.Localization;

namespace RCynQ.Sync.Diagnostics.Checks;

/// <summary>
/// Checks the format of <c>authorized_keys</c> and that every RCynQ-managed key is restricted.
/// </summary>
public sealed class AuthorizedKeysFormatCheck : DiagnosticCheckBase
{
    /// <summary>The authorized keys manager.</summary>
    private readonly AuthorizedKeysManager _keys;

    /// <summary>The localizer of the messages.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="AuthorizedKeysFormatCheck"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="keys">The authorized keys manager.</param>
    public AuthorizedKeysFormatCheck(ILocalizer localizer, AuthorizedKeysManager keys)
        : base("AuthorizedKeysFormat", AllRoles)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _keys = keys;
    }

    /// <inheritdoc />
    public override async Task<DiagnosticResult> RunAsync(DiagnosticContext context, CancellationToken cancellationToken = default)
    {
        // A node that accepts connections on its own TLS port runs no SSH server: nothing reads authorized_keys there,
        // and a missing file is how it is meant to be.
        if (context.Options.Endpoint.Mode == EndpointMode.Direct)
        {
            return Pass(_localizer.Get("Access.NoSshServer"));
        }

        if (!File.Exists(_keys.FilePath))
        {
            return context.Node.Role == NodeRole.Source
                ? Pass(_localizer.Format("Diag.Keys.NotYet", _keys.FilePath))
                : Fail(_localizer.Format("Diag.Keys.Missing", _keys.FilePath), _localizer.Get("Diag.Keys.MissingFix"));
        }

        AuthorizedKeysFile file;
        try
        {
            file = await _keys.ReadAsync(cancellationToken);
        }
        catch (UnauthorizedAccessException)
        {
            return Pass(_localizer.Format("Diag.Keys.NeedsAdmin", _keys.FilePath));
        }

        var unrestricted = file.Entries
            .Where(entry => entry.Key.Comment?.StartsWith("rcynq:", StringComparison.Ordinal) == true && !entry.IsRestricted)
            .Select(entry => entry.LineNumber)
            .ToArray();

        if (file.InvalidLines.Count == 0 && unrestricted.Length == 0)
        {
            return Pass(_localizer.Format("Diag.Keys.Ok", file.Entries.Count));
        }

        var problems = new List<string>();
        if (file.InvalidLines.Count > 0)
        {
            problems.Add(_localizer.Format("Diag.Keys.Unparsable", string.Join(", ", file.InvalidLines)));
        }

        if (unrestricted.Length > 0)
        {
            problems.Add(_localizer.Format("Diag.Keys.Unrestricted", string.Join(", ", unrestricted)));
        }

        return Fail(string.Join("; ", problems), _localizer.Get("Diag.Keys.UnrestrictedFix"));
    }
}
