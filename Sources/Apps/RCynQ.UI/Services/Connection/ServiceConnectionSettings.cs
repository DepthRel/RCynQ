namespace RCynQ.UI.Services.Connection;

/// <summary>
/// How the UI reaches the local service.
/// </summary>
/// <param name="ServiceUri">The loopback base address of the service.</param>
/// <param name="ApiToken">The local API token, or an empty string when it could not be read.</param>
/// <param name="TokenSource">Where the token was read from, for diagnostics.</param>
/// <param name="ConfigPath">The configuration file the service runs with; it may not exist yet.</param>
public sealed record ServiceConnectionSettings(Uri ServiceUri, string ApiToken, string TokenSource, string ConfigPath)
{
    /// <summary>
    /// Gets a value indicating whether <see cref="TokenSource"/> was worked out from the configuration file rather than
    /// named by an argument or the environment. Such a file moves with the data directory of the node, which the first
    /// setup and a reset decide, so it is worked out again whenever the token is read again.
    /// </summary>
    public bool IsTokenFileDerived { get; init; }
}
