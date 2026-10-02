namespace RCynQ.Core.Api;

/// <summary>
/// The answer of an Origin to a Workstation that asks which repositories it offers.
/// </summary>
/// <param name="Names">The names of the registered repositories. Their paths are never told.</param>
/// <param name="Active">The name of the repository the Origin works with now.</param>
public sealed record PeerRepositoriesResponse(IReadOnlyList<string> Names, string? Active);
