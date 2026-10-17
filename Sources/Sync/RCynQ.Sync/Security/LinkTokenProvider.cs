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
    public const string SECRET_NAME = "link-token";

    /// <summary>
    /// The minimum recommended token length.
    /// </summary>
    public const int MINIMUM_LENGTH = 16;

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
            : await _secrets.GetAsync(SECRET_NAME, cancellationToken);

    /// <summary>
    /// Replaces the link token of this node with the one an invitation of its counterpart carried: in the running
    /// options at once and in the configuration file for the next start.
    /// </summary>
    /// <param name="token">The new token.</param>
    /// <param name="configPath">The configuration file of this node.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the operation.</returns>
    /// <exception cref="ArgumentException">The token is shorter than <see cref="MINIMUM_LENGTH"/>.</exception>
    public async Task ReplaceAsync(string token, string configPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configPath);
        if (string.IsNullOrWhiteSpace(token) || token.Trim().Length < MINIMUM_LENGTH)
        {
            throw new ArgumentException("The link token is too short.", nameof(token));
        }

        await Configuration.ConfigurationFileEditor.UpdateAsync(
            configPath,
            section => Configuration.ConfigurationFileEditor.SetProperty(section, nameof(SyncBridgeOptions.LinkToken), token.Trim()),
            cancellationToken);
        _options.LinkToken = token.Trim();
    }

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
