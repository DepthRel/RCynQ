namespace RCynQ.Core.Api;

/// <summary>
/// The body of <c>PUT /config/accept-commits</c>.
/// </summary>
/// <param name="Enabled">Whether this Origin takes commits from the Workstation.</param>
public sealed record AcceptCommitsRequest(bool Enabled);
