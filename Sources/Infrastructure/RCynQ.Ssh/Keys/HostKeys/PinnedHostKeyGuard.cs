using RCynQ.Core.Abstractions.Processes;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Localization;
using RCynQ.Ssh.Commands;

namespace RCynQ.Ssh.Keys.HostKeys;

/// <summary>
/// Default <see cref="IPinnedHostKeyGuard"/>: finds the host key with the pinned fingerprint and records it in the
/// <c>known_hosts</c> file of the node as the only key of the host.
/// </summary>
/// <remarks>
/// <para>
/// A fingerprint does not say which kind of key it belongs to, and an SSH client negotiates one kind per connection:
/// the one it prefers, unless it already knows the host by another. On a first connection it records that one key and
/// does not learn the others. A pin of a key the client would not pick by itself (the RSA key of a host that also has
/// an Ed25519 key) can therefore never be confirmed by looking at what a connection recorded.
/// </para>
/// <para>
/// So the guard asks instead. For each kind of key it opens a connection that offers only that kind and no way to log
/// in; the host presents its key of that kind, and nothing else happens. The key whose fingerprint is the pinned one
/// is the pinned key, whoever delivered it, because the fingerprint is the hash of the key. It is recorded as the only
/// key of the host, and the client from then on asks for exactly that key and checks the proof of its ownership.
/// </para>
/// <para>
/// A peer without a pinned fingerprint, and a peer that runs no SSH server, is not checked. Without a
/// <c>known_hosts</c> file of the node there is nothing to record into and nothing is checked.
/// </para>
/// </remarks>
public sealed class PinnedHostKeyGuard : IPinnedHostKeyGuard, IDisposable
{
    /// <summary>What the client says when a host has no key of the kind asked for; the host itself did answer.</summary>
    private const string NoSuchKeyKind = "no matching host key type";

    /// <summary>
    /// The kinds of host key to ask for, in the order they are tried: the key a client picks by itself first, then the
    /// one earlier versions pinned, then the rest.
    /// </summary>
    private static readonly string[] HostKeyAlgorithms =
    [
        "ssh-ed25519",
        "rsa-sha2-512,rsa-sha2-256",
        "ecdsa-sha2-nistp256",
        "ecdsa-sha2-nistp384",
        "ecdsa-sha2-nistp521"
    ];

    /// <summary>The localizer of the messages.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>The SSH client options.</summary>
    private readonly SshClientOptions _options;

    /// <summary>The SSH command builder.</summary>
    private readonly SshCommandBuilder _commands;

    /// <summary>The process runner.</summary>
    private readonly IProcessRunner _processRunner;

    /// <summary>One check at a time: two of them would rewrite the same file.</summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="PinnedHostKeyGuard"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="options">The SSH client options.</param>
    /// <param name="commands">The SSH command builder.</param>
    /// <param name="processRunner">The process runner.</param>
    public PinnedHostKeyGuard(ILocalizer localizer, SshClientOptions options, SshCommandBuilder commands, IProcessRunner processRunner)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
    }

    /// <inheritdoc />
    public async Task EnsurePinnedAsync(IReadOnlyList<PeerDescriptor> hops, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(hops);
        if (string.IsNullOrWhiteSpace(_options.KnownHostsFile))
        {
            return;
        }

        var knownHosts = Path.GetFullPath(_options.KnownHostsFile);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            for (var i = 0; i < hops.Count; i++)
            {
                var peer = hops[i];
                if (peer.HostKeyFingerprint is null || peer.EndpointMode == EndpointMode.Direct)
                {
                    continue;
                }

                // The hosts before this one are already known by their pinned keys, so the question passes them safely.
                var jumps = hops.Take(i).Select(hop => hop.Ssh).ToArray();
                await EnsurePinnedAsync(knownHosts, peer, jumps, cancellationToken);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Releases the gate.
    /// </summary>
    public void Dispose() => _gate.Dispose();

    /// <summary>
    /// Makes one host known by its pinned key only.
    /// </summary>
    /// <param name="knownHosts">The <c>known_hosts</c> file of the node.</param>
    /// <param name="peer">The pinned peer.</param>
    /// <param name="jumps">The hosts the peer is reached through.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the host is known by its pinned key.</returns>
    /// <exception cref="SshExecutionException">The host owns no key with the pinned fingerprint, or presented no key.</exception>
    private async Task EnsurePinnedAsync(string knownHosts, PeerDescriptor peer, IReadOnlyList<SshEndpoint> jumps, CancellationToken cancellationToken)
    {
        var recorded = KnownHostsFile.Find(knownHosts, peer.Host, peer.SshPort);
        if (recorded.Count == 1 && Matches(peer, recorded[0]))
        {
            return;
        }

        if (recorded.FirstOrDefault(key => Matches(peer, key)) is { } known)
        {
            // The pinned key is on record next to others; those were never checked against the pin.
            KnownHostsFile.Replace(knownHosts, peer.Host, peer.SshPort, known);
            return;
        }

        var presented = new List<SshPublicKey>();
        string? failure = null;
        foreach (var algorithms in HostKeyAlgorithms)
        {
            var (key, error) = await AskAsync(peer.Ssh, jumps, algorithms, cancellationToken);
            if (key is null)
            {
                if (error.Contains(NoSuchKeyKind, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // The host did not get as far as presenting a key; asking for another kind would fail the same way.
                failure = error;
                break;
            }

            if (Matches(peer, key))
            {
                KnownHostsFile.Replace(knownHosts, peer.Host, peer.SshPort, key);
                return;
            }

            presented.Add(key);
        }

        // Whatever was on record is not the pinned key and must not let a connection through.
        KnownHostsFile.Remove(knownHosts, peer.Host, peer.SshPort);
        var endpoint = KnownHostsFile.HostName(peer.Host, peer.SshPort);
        throw new SshExecutionException(presented.Count > 0
            ? _localizer.Format("HostKey.Mismatch", peer.Id, endpoint, peer.HostKeyFingerprint, string.Join(", ", presented.Select(key => $"{key.Fingerprint} ({key.Type})")))
            : _localizer.Format("HostKey.Unreachable", peer.Id, endpoint, peer.HostKeyFingerprint, failure ?? string.Empty).TrimEnd());
    }

    /// <summary>
    /// Asks a host for its key of one kind.
    /// </summary>
    /// <param name="target">The host.</param>
    /// <param name="jumps">The hosts it is reached through.</param>
    /// <param name="algorithms">The kind of key to ask for.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The key the host presented, or <see langword="null"/> with what the client reported.</returns>
    private async Task<(SshPublicKey? Key, string Error)> AskAsync(SshEndpoint target, IReadOnlyList<SshEndpoint> jumps, string algorithms, CancellationToken cancellationToken)
    {
        // ssh runs in the directory of its configuration file; the answer is written there under a name of its own.
        var directory = _commands.WorkingDirectory ?? Path.GetDirectoryName(Path.GetFullPath(_options.KnownHostsFile!))!;
        Directory.CreateDirectory(directory);
        var fileName = $"known_hosts.ask-{Guid.NewGuid():N}";
        var answer = Path.Combine(directory, fileName);
        try
        {
            var result = await _processRunner.RunAsync(
                new ProcessSpec(
                    _commands.Executable,
                    _commands.BuildHostKeyProbe(target, jumps, algorithms, fileName),
                    directory,
                    null,
                    TimeSpan.FromSeconds(_options.ConnectTimeoutSeconds + 15)),
                cancellationToken);
            var key = KnownHostsFile.Find(answer, target.Host, target.Port).FirstOrDefault();
            return (key, LastLine(result.StandardError));
        }
        catch (SyncBridgeException ex) when (ex is not SshExecutionException)
        {
            // ssh could not be started or did not end in time.
            return (null, ex.Message);
        }
        finally
        {
            foreach (var file in Directory.EnumerateFiles(directory, fileName + "*"))
            {
                File.Delete(file);
            }
        }
    }

    /// <summary>
    /// Takes the last line of what the client reported: the reason a connection ended.
    /// </summary>
    /// <param name="text">The error output.</param>
    /// <returns>The last line, or an empty string.</returns>
    private static string LastLine(string text)
        => text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? string.Empty;

    /// <summary>
    /// Tells whether a key is the pinned key of a peer.
    /// </summary>
    /// <param name="peer">The peer.</param>
    /// <param name="key">The key.</param>
    /// <returns><see langword="true"/> when the fingerprints are equal.</returns>
    private static bool Matches(PeerDescriptor peer, SshPublicKey key)
        => HostKeyVerifier.FingerprintsMatch(peer.HostKeyFingerprint!, key.Fingerprint);
}
