using System.Security.Cryptography;
using System.Text;
using RCynQ.Core.Abstractions.Storage;
using RCynQ.Core.Config;

namespace RCynQ.Sync.Security;

/// <summary>
/// Provides and checks the shared link token.
/// </summary>
public sealed class LinkTokenProvider
{
    /// <summary>
    /// The secret store entry holding the token when it is not set in configuration.
    /// </summary>
    public const string SecretName = "link-token";

    /// <summary>
    /// The minimum recommended token length.
    /// </summary>
    public const int MinimumLength = 16;

    /// <summary>
    /// The node options.
    /// </summary>
    private readonly SyncBridgeOptions _options;

    /// <summary>
    /// The secret store.
    /// </summary>
    private readonly ISecretStore _secrets;

    /// <summary>
    /// Initializes a new instance of the <see cref="LinkTokenProvider"/> class.
    /// </summary>
    /// <param name="options">The node options.</param>
    /// <param name="secrets">The secret store.</param>
    public LinkTokenProvider(SyncBridgeOptions options, ISecretStore secrets)
    {
        _options = options;
        _secrets = secrets;
    }

    /// <summary>
    /// Gets the configured token: <see cref="SyncBridgeOptions.LinkToken"/> or the secret store entry.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The token, or <see langword="null"/> when none is configured.</returns>
    public async Task<string?> GetTokenAsync(CancellationToken cancellationToken = default)
        => !string.IsNullOrWhiteSpace(_options.LinkToken)
            ? _options.LinkToken.Trim()
            : await _secrets.GetAsync(SecretName, cancellationToken);

    /// <summary>
    /// Compares tokens in constant time.
    /// </summary>
    /// <param name="expected">The expected token.</param>
    /// <param name="actual">The presented token.</param>
    /// <returns><see langword="true"/> when a token is configured and both match.</returns>
    public static bool Matches(string? expected, string? actual)
    {
        if (string.IsNullOrEmpty(expected) || actual is null)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)),
            SHA256.HashData(Encoding.UTF8.GetBytes(actual)));
    }
}
