using RCynQ.Core.Abstractions;
using RCynQ.Core.Config;

namespace RCynQ.Ssh.Keys.HostKeys;

/// <summary>
/// The result of a host key verification.
/// </summary>
/// <param name="Status">The status.</param>
/// <param name="Expected">The expected fingerprint, or <see langword="null"/>.</param>
/// <param name="Actual">The fingerprints presented by the host.</param>
public sealed record HostKeyVerificationResult(HostKeyVerificationStatus Status, string? Expected, IReadOnlyList<string> Actual);
