namespace RCynQ.Core.Patches;

/// <summary>
/// Transport-level facts about a received patch request that the receiver must validate.
/// </summary>
/// <param name="SenderNodeId">The value of the <c>X-Node-Id</c> header, or <see langword="null"/> when missing.</param>
/// <param name="LinkToken">The value of the <c>X-Link-Token</c> header, or <see langword="null"/> when missing.</param>
public sealed record PatchReceiveContext(string? SenderNodeId, string? LinkToken);
