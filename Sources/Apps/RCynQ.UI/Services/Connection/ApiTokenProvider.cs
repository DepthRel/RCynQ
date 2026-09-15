namespace RCynQ.UI.Services.Connection;

/// <summary>
/// Supplies the local API token, re-reading the token file when the token is missing or was rejected.
/// </summary>
/// <remarks>
/// The UI may start before the service has created <c>keys/api.token</c>; a file-backed token is therefore resolved
/// lazily instead of once at startup. A token passed explicitly (argument or environment) is never re-read.
/// </remarks>
public sealed class ApiTokenProvider
{
    /// <summary>The <see cref="ServiceConnectionSettings.TokenSource"/> of an explicitly passed token.</summary>
    public const string ArgumentSource = "argument";

    /// <summary>The connection settings.</summary>
    private readonly ServiceConnectionSettings _settings;

    /// <summary>Guards the cached token.</summary>
    private readonly object _gate = new();

    /// <summary>The cached token, or <see langword="null"/> when it must be read again.</summary>
    private string? _token;

    /// <summary>
    /// Initializes a new instance of the <see cref="ApiTokenProvider"/> class.
    /// </summary>
    /// <param name="settings">The connection settings.</param>
    public ApiTokenProvider(ServiceConnectionSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _token = string.IsNullOrEmpty(settings.ApiToken) ? null : settings.ApiToken;
    }

    /// <summary>
    /// Gets a value indicating whether the token comes from a file.
    /// </summary>
    public bool IsFileBacked => !string.Equals(_settings.TokenSource, ArgumentSource, StringComparison.Ordinal);

    /// <summary>
    /// Gets the current token.
    /// </summary>
    /// <returns>The token, or an empty string when none is available yet.</returns>
    public string GetToken()
    {
        lock (_gate)
        {
            if (string.IsNullOrEmpty(_token) && IsFileBacked)
            {
                _token = TryReadFile(_settings.TokenSource);
            }

            return _token ?? string.Empty;
        }
    }

    /// <summary>
    /// Forgets a file-backed token so that the next request reads the file again.
    /// </summary>
    public void Invalidate()
    {
        if (!IsFileBacked)
        {
            return;
        }

        lock (_gate)
        {
            _token = null;
        }
    }

    /// <summary>
    /// Reads a token file.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <returns>The trimmed token, or <see langword="null"/> when the file is missing, empty or unreadable.</returns>
    private static string? TryReadFile(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var token = File.ReadAllText(path).Trim();
            return token.Length == 0 ? null : token;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
