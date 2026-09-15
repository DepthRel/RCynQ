using RCynQ.Core.Enums;

namespace RCynQ.Core.Api;

/// <summary>
/// Body of <c>POST /patches/{id}/resolve</c>.
/// </summary>
/// <param name="Policy">The user's conflict policy.</param>
public sealed record ResolvePatchRequest(ConflictPolicy Policy);
