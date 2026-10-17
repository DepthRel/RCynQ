using RCynQ.Ssh.Enums;

namespace RCynQ.Ssh.Server;

/// <summary>
/// What the planner needs to know about the computer: the system, the Linux distribution and the tools on it.
/// </summary>
/// <param name="Platform">The operating system family.</param>
/// <param name="Distributions">
/// The Linux distribution and the ones it derives from (<c>ID</c> and <c>ID_LIKE</c> of <c>/etc/os-release</c>), lower
/// case; empty elsewhere.
/// </param>
/// <param name="Tools">The names of the tools found on the computer, such as <c>apt-get</c> or <c>pkexec</c>.</param>
public sealed record HostSystem(HostPlatform Platform, IReadOnlyList<string> Distributions, IReadOnlySet<string> Tools)
{
    /// <summary>
    /// Tells whether a tool was found on the computer.
    /// </summary>
    /// <param name="name">The name of the tool.</param>
    /// <returns><see langword="true"/> when the tool exists.</returns>
    public bool Has(string name) => Tools.Contains(name);

    /// <summary>
    /// Tells whether the distribution is the given one or derives from it.
    /// </summary>
    /// <param name="name">The distribution identifier, lower case.</param>
    /// <returns><see langword="true"/> when the distribution matches.</returns>
    public bool IsLike(string name) => Distributions.Contains(name, StringComparer.Ordinal);
}
