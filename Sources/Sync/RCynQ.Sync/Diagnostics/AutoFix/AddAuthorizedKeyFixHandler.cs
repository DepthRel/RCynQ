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

    /// <summary>
    /// Initializes a new instance of the <see cref="AddAuthorizedKeyFixHandler"/> class.
    /// </summary>
    /// <param name="keys">The authorized keys manager.</param>
    /// <param name="node">The local node.</param>
    public AddAuthorizedKeyFixHandler(AuthorizedKeysManager keys, NodeConfig node)
    {
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
            throw new ArgumentException(Texts.Get("Fix.AddKeyParameters"));
        }

        var key = SshPublicKey.Parse(keyText);
        AuthorizedKeysManager.BuildLineForRole(_node.Role, key, target, purpose);
        var added = await _keys.AddAsync(purpose, key, target, cancellationToken);
        return new AutoFixResult(true, added ? Texts.Format("Fix.KeyAdded", purpose, key.Fingerprint) : Texts.Format("Fix.KeyAlready", key.Fingerprint));
    }
}
