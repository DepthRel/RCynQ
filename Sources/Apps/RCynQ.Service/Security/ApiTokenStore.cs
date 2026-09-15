using System.Security.Cryptography;
using System.Text;
using RCynQ.Core.Abstractions.Storage;
using RCynQ.Core.Data.Storage;

namespace RCynQ.Service.Security;

/// <summary>
/// Provides the local management API token.
/// </summary>
/// <remarks>
/// The token is a random 256-bit value stored in <c>keys/api.token</c> with owner-only permissions on Unix-like systems.
/// Local clients (UI, CLI) read it from the file; peers connected through the SSH forward cannot, so they cannot use
/// the management API even though their connections also arrive on the loopback interface.
/// </remarks>
public sealed class ApiTokenStore
{
    /// <summary>The application data factory.</summary>
    private readonly IAppDataFactory _appData;

    /// <summary>Guards token creation.</summary>
    private readonly object _gate = new();

    /// <summary>The cached token.</summary>
    private string? _token;

    /// <summary>
    /// Initializes a new instance of the <see cref="ApiTokenStore"/> class.
    /// </summary>
    /// <param name="appData">The application data factory.</param>
    public ApiTokenStore(IAppDataFactory appData)
    {
        _appData = appData;
    }

    /// <summary>
    /// Gets the token, creating it on first use.
    /// </summary>
    /// <returns>The token.</returns>
    public string GetOrCreate()
    {
        if (_token is not null)
        {
            return _token;
        }

        lock (_gate)
        {
            if (_token is not null)
            {
                return _token;
            }

            var path = _appData.Create().ApiTokenPath;
            if (!File.Exists(path) || string.IsNullOrWhiteSpace(File.ReadAllText(path)))
            {
                var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
                File.WriteAllText(path, token);
                FilePermissions.RestrictFileToOwner(path);
            }

            _token = File.ReadAllText(path).Trim();
            return _token;
        }
    }

    /// <summary>
    /// Compares a presented token with the stored one in constant time.
    /// </summary>
    /// <param name="presented">The presented token.</param>
    /// <returns><see langword="true"/> when the token matches.</returns>
    public bool IsValid(string? presented)
    {
        if (string.IsNullOrEmpty(presented))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(GetOrCreate())),
            SHA256.HashData(Encoding.UTF8.GetBytes(presented)));
    }
}
