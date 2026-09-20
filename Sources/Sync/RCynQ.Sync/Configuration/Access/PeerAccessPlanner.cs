using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Ssh.Keys;
using RCynQ.Ssh.Keys.AuthorizedKeys;

namespace RCynQ.Sync.Configuration.Access;

/// <summary>
/// Decides which neighbours may connect to this node and what exactly each key may do.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Dest: a Source key may only forward to the local RCynQ endpoint (and, where supported, run <c>git-upload-pack</c>
/// for the repository).</item>
/// <item>Source: a Dest key may only forward to the local RCynQ endpoint.</item>
/// <item>Mediator: a Source key may only forward to the SSH servers of the Dest peers and a Dest key only to the SSH
/// servers of the Source peers.</item>
/// </list>
/// Every line is restricted: no shell, no terminal, no agent or X11 forwarding.
/// </remarks>
public static class PeerAccessPlanner
{
    /// <summary>
    /// Builds the lines for the neighbours.
    /// </summary>
    /// <param name="node">The local node.</param>
    /// <param name="listenPort">The port of the local RCynQ endpoint.</param>
    /// <param name="peers">The imported neighbours.</param>
    /// <param name="allowGitCommand">Whether Source keys on Dest may run <c>git-upload-pack</c> (not on Windows, where the forced command runs through <c>cmd.exe</c>).</param>
    /// <returns>The plan.</returns>
    public static PeerAccessPlan Plan(NodeConfig node, int listenPort, IReadOnlyList<PeerDescriptor> peers, bool allowGitCommand)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(peers);

        var entries = new List<PlannedAccess>();
        var missing = new List<PeerDescriptor>();
        var waiting = new List<PeerDescriptor>();
        var endpoint = $"127.0.0.1:{listenPort.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

        foreach (var peer in peers.OrderBy(peer => peer.Id.Value, StringComparer.Ordinal))
        {
            IReadOnlyList<string> targets;
            string? repoPath = null;
            switch (node.Role)
            {
                case NodeRole.Dest when peer.Role == NodeRole.Source:
                    targets = new[] { endpoint };
                    repoPath = allowGitCommand ? node.RepoPath : null;
                    break;
                case NodeRole.Source when peer.Role == NodeRole.Dest:
                    targets = new[] { endpoint };
                    break;
                case NodeRole.Mediator when peer.Role is NodeRole.Source or NodeRole.Dest:
                    var opposite = peer.Role == NodeRole.Source ? NodeRole.Dest : NodeRole.Source;
                    // The opposite endpoint is reached on its SSH server, or on its own TLS port when it runs none.
                    targets = peers.Where(other => other.Role == opposite)
                        .Select(other => FormatTarget(other.Host, other.ReachablePort))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                    break;
                default:
                    continue;
            }

            var key = TryParseKey(peer.SshPublicKey);
            if (key is null)
            {
                missing.Add(peer);
                continue;
            }

            if (targets.Count == 0)
            {
                waiting.Add(peer);
                continue;
            }

            entries.Add(new PlannedAccess(peer, BuildLine(key, targets, repoPath, peer)));
        }

        return new PeerAccessPlan(entries, missing, waiting);
    }

    /// <summary>
    /// Formats a <c>permitopen</c> target, bracketing IPv6 addresses.
    /// </summary>
    /// <param name="host">The host.</param>
    /// <param name="port">The port.</param>
    /// <returns>The target.</returns>
    public static string FormatTarget(string host, int port)
    {
        var text = port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return host.Contains(':') ? $"[{host}]:{text}" : $"{host}:{text}";
    }

    /// <summary>
    /// Builds the line, falling back to forward-only access when the repository path cannot be used in a forced command.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="targets">The forward targets.</param>
    /// <param name="repoPath">The repository for <c>git-upload-pack</c>, or <see langword="null"/>.</param>
    /// <param name="peer">The neighbour.</param>
    /// <returns>The line.</returns>
    private static string BuildLine(SshPublicKey key, IReadOnlyList<string> targets, string? repoPath, PeerDescriptor peer)
    {
        var comment = $"rcynq:{peer.Id.Value}";
        if (repoPath is not null)
        {
            try
            {
                return AuthorizedKeysManager.BuildForwardLine(key, targets, repoPath, comment);
            }
            catch (ArgumentException)
            {
                // The repository path contains characters that cannot be quoted safely; branches then stay unavailable.
            }
        }

        return AuthorizedKeysManager.BuildForwardLine(key, targets, null, comment);
    }

    /// <summary>
    /// Parses a stored key.
    /// </summary>
    /// <param name="line">The key line.</param>
    /// <returns>The key, or <see langword="null"/> when missing or invalid.</returns>
    private static SshPublicKey? TryParseKey(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        try
        {
            return SshPublicKey.Parse(line);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
