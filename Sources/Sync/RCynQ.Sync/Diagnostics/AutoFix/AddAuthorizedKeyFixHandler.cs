using RCynQ.Core.Config;
using RCynQ.Core.Diagnostics;
using RCynQ.Ssh.Keys;
using RCynQ.Ssh.Keys.AuthorizedKeys;
using RCynQ.Localization;

namespace RCynQ.Sync.Diagnostics.AutoFix;

/// <summary>
/// Adds a restricted <c>authorized_keys</c> line allowed for the local role.
/// </summary>
public sealed class AddAuthorizedKeyFixHandler : IAutoFixHandler
{
    /// <summary>The authorized keys manager.</summary>
    private readonly AuthorizedKeysManager _keys;

    /// <summary>The local node.</summary>
    private readonly NodeConfig _node;

    /// <summary>The localizer of the messages.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="AddAuthorizedKeyFixHandler"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="keys">The authorized keys manager.</param>
    /// <param name="node">The local node.</param>
    public AddAuthorizedKeyFixHandler(ILocalizer localizer, AuthorizedKeysManager keys, NodeConfig node)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _keys = keys;
        _node = node;
    }

    /// <inheritdoc />
    public DiagnosticFixKind Kind => DiagnosticFixKind.AddRestrictedAuthorizedKey;

    /// <inheritdoc />
    public async Task<AutoFixResult> ExecuteAsync(DiagnosticFix fix, CancellationToken cancellationToken)
    {
        if (!fix.Parameters.TryGetValue("purpose", out var purposeText) || !Enum.TryParse<AuthorizedKeyPurpose>(purposeText, true, out var purpose)
            || !fix.Parameters.TryGetValue("publicKey", out var keyText) || !fix.Parameters.TryGetValue("target", out var target))
        {
            throw new ArgumentException(_localizer.Get("Fix.AddKeyParameters"));
        }

        var key = SshPublicKey.Parse(keyText);
        AuthorizedKeysManager.BuildLineForRole(_node.Role, key, target, purpose);
        var added = await _keys.AddAsync(purpose, key, target, cancellationToken);
        return new AutoFixResult(true, added ? _localizer.Format("Fix.KeyAdded", purpose, key.Fingerprint) : _localizer.Format("Fix.KeyAlready", key.Fingerprint));
    }
}
