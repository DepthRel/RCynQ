using RCynQ.Ssh.Enums;

namespace RCynQ.Ssh.Server;

/// <summary>
/// Describes the computer from the running system: <see cref="OperatingSystem"/>, <c>/etc/os-release</c> and the tools
/// found in the search path.
/// </summary>
public sealed class SystemHostSystemSource : IHostSystemSource
{
    /// <summary>The file that names the Linux distribution.</summary>
    private const string OS_RELEASE = "/etc/os-release";

    /// <summary>The tools the planner may use.</summary>
    private static readonly string[] _knownTools =
    {
        "apt-get", "dnf", "zypper", "pacman", "systemctl", "pkexec", "ufw", "firewall-cmd"
    };

    /// <summary>
    /// Folders of system tools that are often missing from the search path of a user who is not an administrator.
    /// </summary>
    private static readonly string[] _systemFolders = { "/usr/local/sbin", "/usr/local/bin", "/usr/sbin", "/usr/bin", "/sbin", "/bin" };

    /// <inheritdoc />
    public HostSystem Describe()
    {
        var platform = OperatingSystem.IsWindows() ? HostPlatform.Windows
            : OperatingSystem.IsLinux() ? HostPlatform.Linux
            : OperatingSystem.IsMacOS() ? HostPlatform.MacOs
            : HostPlatform.Other;
        if (platform != HostPlatform.Linux)
        {
            return new HostSystem(platform, Array.Empty<string>(), new HashSet<string>(StringComparer.Ordinal));
        }

        var release = File.Exists(OS_RELEASE) ? ParseOsRelease(File.ReadAllText(OS_RELEASE)) : Array.Empty<string>();
        var folders = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Concat(_systemFolders);
        return new HostSystem(platform, release, FindTools(folders, File.Exists));
    }

    /// <summary>
    /// Reads the distribution and the ones it derives from out of the text of <c>/etc/os-release</c>.
    /// </summary>
    /// <param name="text">The text of the file.</param>
    /// <returns>The value of <c>ID</c> followed by the words of <c>ID_LIKE</c>, lower case.</returns>
    public static IReadOnlyList<string> ParseOsRelease(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var values = text.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#') && line.Contains('='))
            .Select(line => (Key: line[..line.IndexOf('=')], Value: line[(line.IndexOf('=') + 1)..].Trim('"', '\'')))
            .GroupBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last().Value, StringComparer.Ordinal);

        var id = values.GetValueOrDefault("ID");
        var like = values.GetValueOrDefault("ID_LIKE");
        return (id is null ? Enumerable.Empty<string>() : new[] { id })
            .Concat(like?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>())
            .Select(name => name.ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// Finds the known tools in the given folders.
    /// </summary>
    /// <param name="folders">The folders to look in.</param>
    /// <param name="exists">Tells whether a file exists.</param>
    /// <returns>The names of the tools found.</returns>
    public static IReadOnlySet<string> FindTools(IEnumerable<string> folders, Func<string, bool> exists)
    {
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(exists);

        var list = folders.Distinct(StringComparer.Ordinal).ToArray();
        return _knownTools
            .Where(tool => list.Any(folder => exists(Path.Combine(folder, tool))))
            .ToHashSet(StringComparer.Ordinal);
    }
}
