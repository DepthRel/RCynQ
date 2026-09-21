using RCynQ.Core.Config;

namespace RCynQ.Ssh.Routing;

/// <summary>
/// Builds the <c>-J</c> jump host argument.
/// </summary>
public static class ProxyJumpBuilder
{
    /// <summary>
    /// Builds <c>-J user@host:port,...</c> for the given jump hosts.
    /// </summary>
    /// <param name="jumps">The jump hosts in traversal order.</param>
    /// <returns>An empty list for a direct connection, otherwise the two arguments <c>-J</c> and the jump specification.</returns>
    public static IReadOnlyList<string> Build(IReadOnlyList<SshEndpoint> jumps)
    {
        ArgumentNullException.ThrowIfNull(jumps);
        return jumps.Count == 0
            ? Array.Empty<string>()
            : new[] { "-J", string.Join(',', jumps.Select(jump => jump.JumpSpec)) };
    }
}
