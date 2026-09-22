namespace RCynQ.Core.Api;

/// <summary>
/// The request of a peer for the branches of the repository: which commits it already has.
/// </summary>
/// <remarks>
/// The receiver leaves out everything reachable from the commits it knows, so only new objects travel. A peer without a
/// repository, or without any common history, sends an empty list and gets the whole repository.
/// </remarks>
/// <param name="Have">The full object names (40 or 64 hexadecimal characters) of commits the peer already has.</param>
public sealed record GitBundleRequest(IReadOnlyList<string> Have);
