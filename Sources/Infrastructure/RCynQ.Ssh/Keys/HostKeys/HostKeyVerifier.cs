using RCynQ.Core.Abstractions;
using RCynQ.Core.Abstractions.Processes;
using RCynQ.Core.Config;

namespace RCynQ.Ssh.Keys.HostKeys;

/// <summary>
/// Verifies SSH host keys against configured fingerprints.
/// </summary>
/// <remarks>
/// Host keys are retrieved with <c>ssh-keyscan</c>, which connects directly; peers reachable only through jump hosts
/// are verified by OpenSSH's own <c>known_hosts</c> handling instead.
/// </remarks>
public sealed class HostKeyVerifier
{
    /// <summary>
    /// The process runner.
    /// </summary>
    private readonly IProcessRunner _processRunner;

    /// <summary>
    /// The SSH options.
    /// </summary>
    private readonly SshClientOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="HostKeyVerifier"/> class.
    /// </summary>
    /// <param name="processRunner">The process runner.</param>
    /// <param name="options">The SSH options.</param>
    public HostKeyVerifier(IProcessRunner processRunner, SshClientOptions options)
    {
        _processRunner = processRunner;
        _options = options;
    }

    /// <summary>
    /// Compares two fingerprints after normalization.
    /// </summary>
    /// <param name="expected">The expected fingerprint.</param>
    /// <param name="actual">The actual fingerprint.</param>
    /// <returns><see langword="true"/> when they match.</returns>
    public static bool FingerprintsMatch(string expected, string actual)
        => SshPublicKey.NormalizeFingerprint(expected) == SshPublicKey.NormalizeFingerprint(actual);

    /// <summary>
    /// Retrieves the fingerprints of all host keys of an endpoint.
    /// </summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The fingerprints; empty when the host is unreachable.</returns>
    public async Task<IReadOnlyList<string>> GetHostFingerprintsAsync(SshEndpoint endpoint, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        var invariant = System.Globalization.CultureInfo.InvariantCulture;
        var result = await _processRunner.RunAsync(
            new ProcessSpec(
                _options.SshKeyscanExecutable,
                new[] { "-T", _options.ConnectTimeoutSeconds.ToString(invariant), "-p", endpoint.Port.ToString(invariant), "--", endpoint.Host },
                null,
                null,
                TimeSpan.FromSeconds(_options.ConnectTimeoutSeconds + 10)),
            cancellationToken);

        var fingerprints = new List<string>();
        foreach (var line in result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (line.StartsWith('#'))
            {
                continue;
            }

            var parts = line.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3)
            {
                continue;
            }

            try
            {
                fingerprints.Add(new SshPublicKey(parts[1], parts[2].Split(' ')[0], null).Fingerprint);
            }
            catch (FormatException)
            {
                // Unsupported key types are ignored.
            }
        }

        return fingerprints.Distinct(StringComparer.Ordinal).ToArray();
    }

    /// <summary>
    /// Verifies the host key of a peer.
    /// </summary>
    /// <param name="peer">The peer.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The verification result.</returns>
    public async Task<HostKeyVerificationResult> VerifyAsync(PeerDescriptor peer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(peer);
        if (peer.HostKeyFingerprint is null)
        {
            return new HostKeyVerificationResult(HostKeyVerificationStatus.NotConfigured, null, Array.Empty<string>());
        }

        var actual = await GetHostFingerprintsAsync(peer.Ssh, cancellationToken);
        var status = actual.Count == 0
            ? HostKeyVerificationStatus.Unreachable
            : actual.Any(fingerprint => FingerprintsMatch(peer.HostKeyFingerprint, fingerprint))
                ? HostKeyVerificationStatus.Verified
                : HostKeyVerificationStatus.Mismatch;
        return new HostKeyVerificationResult(status, peer.HostKeyFingerprint, actual);
    }
}
