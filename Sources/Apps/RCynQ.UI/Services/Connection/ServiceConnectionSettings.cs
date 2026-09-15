namespace RCynQ.UI.Services.Connection;

/// <summary>
/// How the UI reaches the local service.
/// </summary>
/// <param name="ServiceUri">The loopback base address of the service.</param>
/// <param name="ApiToken">The local API token, or an empty string when it could not be read.</param>
/// <param name="TokenSource">Where the token was read from, for diagnostics.</param>
/// <param name="ConfigPath">The configuration file the service runs with; it may not exist yet.</param>
public sealed record ServiceConnectionSettings(Uri ServiceUri, string ApiToken, string TokenSource, string ConfigPath);
