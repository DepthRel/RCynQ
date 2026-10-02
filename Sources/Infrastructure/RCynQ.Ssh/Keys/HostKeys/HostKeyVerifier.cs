using RCynQ.Core.Abstractions.Processes;
using RCynQ.Core.Config;
using RCynQ.Ssh.Enums;

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
    /// <returns>
    /// The fingerprints, the key an SSH client prefers first; empty when the host is unreachable.
    /// </returns>
    /// <remarks>
    /// A client records the first kind of key in its order of preference that the server offers. The export pins the
    /// first fingerprint of this list, so the order here is the order of the client: the pinned key is then the one a
    /// neighbour records on its first connection.
    /// </remarks>
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

        var keys = new List<SshPublicKey>();
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
                keys.Add(new SshPublicKey(parts[1], parts[2].Split(' ')[0], null));
            }
            catch (FormatException)
            {
                // Unsupported key types are ignored.
            }
        }

        return keys
            .OrderBy(key => Preference(key.Type))
            .Select(key => key.Fingerprint)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// The kinds of host keys in the order an OpenSSH client prefers them.
    /// </summary>
    private static readonly string[] _clientPreference =
    {
        "ssh-ed25519", "ecdsa-sha2-nistp256", "ecdsa-sha2-nistp384", "ecdsa-sha2-nistp521",
        "sk-ssh-ed25519@openssh.com", "sk-ecdsa-sha2-nistp256@openssh.com", "ssh-rsa"
    };

    /// <summary>
    /// Gets the position of a kind of key in the preference of a client.
    /// </summary>
    /// <param name="type">The kind of key.</param>
    /// <returns>The position; kinds the list does not name come last.</returns>
    private static int Preference(string type)
    {
        var index = Array.IndexOf(_clientPreference, type);
        return index < 0 ? _clientPreference.Length : index;
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
