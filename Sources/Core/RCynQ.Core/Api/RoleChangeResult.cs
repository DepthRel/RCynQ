namespace RCynQ.Core.Api;

/// <summary>
/// Result of a role change request.
/// </summary>
/// <param name="Saved">Whether the configuration file was updated.</param>
/// <param name="RestartRequired">Whether the service must be restarted to apply the change.</param>
/// <param name="Message">A human-readable explanation.</param>
public sealed record RoleChangeResult(bool Saved, bool RestartRequired, string Message);
