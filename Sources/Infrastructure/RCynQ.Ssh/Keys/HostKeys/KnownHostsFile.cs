using System.Security.Cryptography;
using System.Text;

namespace RCynQ.Ssh.Keys.HostKeys;

/// <summary>
/// Reads and edits a <c>known_hosts</c> file: the host keys the SSH client recorded.
/// </summary>
/// <remarks>
/// A line is <c>hosts keytype key [comment]</c>, optionally preceded by a marker (<c>@cert-authority</c>,
/// <c>@revoked</c>). <c>hosts</c> is a comma-separated list of names; a host on another port than 22 is written as
/// <c>[host]:port</c>; a hashed name is <c>|1|salt|hash</c> with <c>hash = HMAC-SHA1(salt, name)</c>. Lines with a
/// marker are not host keys of a host and are left alone.
/// </remarks>
public static class KnownHostsFile
{
    /// <summary>
    /// The default SSH port, for which a host is recorded by its bare name.
    /// </summary>
    public const int DEFAULT_PORT = 22;

    /// <summary>
    /// Builds the name under which the SSH client records a host.
    /// </summary>
    /// <param name="host">The host name or address.</param>
    /// <param name="port">The SSH port.</param>
    /// <returns>The host name for port 22, <c>[host]:port</c> otherwise.</returns>
    public static string HostName(string host, int port)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        var name = host.Trim().Trim('[', ']').ToLowerInvariant();
        return port == DEFAULT_PORT ? name : $"[{name}]:{port.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
    }

    /// <summary>
    /// Finds the keys recorded for a host.
    /// </summary>
    /// <param name="path">The <c>known_hosts</c> file.</param>
    /// <param name="host">The host name or address.</param>
    /// <param name="port">The SSH port.</param>
    /// <returns>The recorded keys; empty when the file does not exist or holds no key of the host.</returns>
    public static IReadOnlyList<SshPublicKey> Find(string path, string host, int port)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var name = HostName(host, port);
        var keys = new List<SshPublicKey>();
        foreach (var line in ReadLines(path))
        {
            if (TryParse(line, name) is { } key)
            {
                keys.Add(key);
            }
        }

        return keys;
    }

    /// <summary>
    /// Removes the keys recorded for a host, so the next connection records the key the host presents then.
    /// </summary>
    /// <param name="path">The <c>known_hosts</c> file.</param>
    /// <param name="host">The host name or address.</param>
    /// <param name="port">The SSH port.</param>
    /// <returns>The number of removed lines.</returns>
    /// <remarks>
    /// A line that names several hosts keeps the other ones. A hashed line names one host and is removed whole.
    /// </remarks>
    public static int Remove(string path, string host, int port)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Rewrite(path, HostName(host, port), null);
    }

    /// <summary>
    /// Makes a host known by one key and no other: the keys recorded for it are removed and the given one is recorded.
    /// </summary>
    /// <param name="path">The <c>known_hosts</c> file; it and its directory are created when missing.</param>
    /// <param name="host">The host name or address.</param>
    /// <param name="port">The SSH port.</param>
    /// <param name="key">The key the host is known by from now on.</param>
    /// <remarks>
    /// With a single key on record the SSH client asks the host for a key of that kind and refuses any other key, which
    /// is how a pinned fingerprint is enforced. The records of other hosts and the marker lines are left alone.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    public static void Replace(string path, string host, int port, SshPublicKey key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(key);
        Rewrite(path, HostName(host, port), key);
    }

    /// <summary>
    /// Rewrites the file without the keys of a host and, when a key is given, with that key recorded for it.
    /// </summary>
    /// <param name="path">The <c>known_hosts</c> file.</param>
    /// <param name="name">The name of the host in the file.</param>
    /// <param name="key">The key to record for the host, or <see langword="null"/> to record none.</param>
    /// <returns>The number of removed lines.</returns>
    private static int Rewrite(string path, string name, SshPublicKey? key)
    {
        var lines = ReadLines(path);
        var kept = new List<string>(lines.Count + 1);
        var removed = 0;
        foreach (var line in lines)
        {
            if (TryParse(line, name) is null)
            {
                kept.Add(line);
                continue;
            }

            removed++;
            var fields = line.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries);
            var others = fields[0].Split(',').Where(pattern => !Names(pattern, name)).ToArray();
            if (others.Length > 0)
            {
                kept.Add(string.Join(',', others) + " " + fields[1]);
            }
        }

        if (key is not null)
        {
            kept.Add($"{name} {key.Type} {key.Blob}");
        }
        else if (removed == 0)
        {
            return 0;
        }

        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var temp = full + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temp, kept.Count == 0 ? string.Empty : string.Join('\n', kept) + "\n", new UTF8Encoding(false));
            File.Move(temp, full, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }

        return removed;
    }

    /// <summary>
    /// Reads the lines of the file.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <returns>The lines; empty when the file does not exist.</returns>
    private static IReadOnlyList<string> ReadLines(string path)
        => File.Exists(path)
            ? File.ReadAllLines(path).Where(line => line.Length > 0).ToArray()
            : Array.Empty<string>();

    /// <summary>
    /// Parses a line and returns its key when the line records a key of the given host.
    /// </summary>
    /// <param name="line">The line.</param>
    /// <param name="name">The name the host is recorded under.</param>
    /// <returns>The key, or <see langword="null"/> for a comment, a marker line, another host or an unreadable key.</returns>
    private static SshPublicKey? TryParse(string line, string name)
    {
        var text = line.Trim();
        if (text.Length == 0 || text[0] is '#' or '@')
        {
            return null;
        }

        var fields = text.Split((char[]?)null, 4, StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length < 3 || !fields[0].Split(',').Any(pattern => Names(pattern, name)))
        {
            return null;
        }

        try
        {
            return new SshPublicKey(fields[1], fields[2], null);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// Tells whether an entry of the host list of a line names the given host.
    /// </summary>
    /// <param name="pattern">The entry: a name or a hashed name.</param>
    /// <param name="name">The name the host is recorded under.</param>
    /// <returns><see langword="true"/> when the entry names the host.</returns>
    private static bool Names(string pattern, string name)
    {
        if (!pattern.StartsWith("|1|", StringComparison.Ordinal))
        {
            return string.Equals(pattern, name, StringComparison.OrdinalIgnoreCase);
        }

        var parts = pattern.Split('|');
        if (parts.Length != 4)
        {
            return false;
        }

        try
        {
            // A hashed name: the format of HashKnownHosts, an HMAC-SHA1 that only hides the name, it protects nothing.
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
#pragma warning disable CA5350 // The algorithm is fixed by the file format of OpenSSH.
            var actual = HMACSHA1.HashData(salt, Encoding.UTF8.GetBytes(name));
#pragma warning restore CA5350
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
