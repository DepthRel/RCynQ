namespace RCynQ.Service.Security;

/// <summary>
/// Settings of the direct endpoint listener, used to tell its requests from local API requests.
/// </summary>
/// <param name="Port">The TCP port of the direct endpoint.</param>
public sealed record PublicEndpointOptions(int Port);
