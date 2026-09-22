namespace RCynQ.Core.Patches;

/// <summary>
/// The state of an incoming patch whose manifest was accepted and whose content is still being transferred.
/// </summary>
/// <param name="PatchId">The patch identifier.</param>
/// <param name="MissingContent">The SHA-256 hashes of the content the receiver does not have yet, in manifest order.</param>
public sealed record PatchSession(Guid PatchId, IReadOnlyList<string> MissingContent);
