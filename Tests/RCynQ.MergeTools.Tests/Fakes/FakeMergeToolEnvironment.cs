using RCynQ.MergeTools.Enums;

namespace RCynQ.MergeTools.Tests.Fakes;

/// <summary>
/// A machine made of a list of files: the folders are the ones the files are in.
/// </summary>
public sealed class FakeMergeToolEnvironment : IMergeToolEnvironment
{
    /// <summary>The environment variables.</summary>
    private readonly Dictionary<string, string> _variables = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The files.</summary>
    private readonly HashSet<string> _files;

    /// <summary>
    /// Initializes a new instance of the <see cref="FakeMergeToolEnvironment"/> class.
    /// </summary>
    /// <param name="platform">The operating system.</param>
    /// <param name="home">The home directory.</param>
    public FakeMergeToolEnvironment(MergeToolPlatform platform, string home)
    {
        Platform = platform;
        HomeDirectory = home;
        _files = new HashSet<string>(platform == MergeToolPlatform.Linux ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public MergeToolPlatform Platform { get; }

    /// <inheritdoc />
    public string HomeDirectory { get; }

    /// <summary>
    /// Sets an environment variable.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="value">The value.</param>
    /// <returns>The machine.</returns>
    public FakeMergeToolEnvironment WithVariable(string name, string value)
    {
        _variables[name] = value;
        return this;
    }

    /// <summary>
    /// Puts files on the machine.
    /// </summary>
    /// <param name="paths">The full paths.</param>
    /// <returns>The machine.</returns>
    public FakeMergeToolEnvironment WithFiles(params string[] paths)
    {
        foreach (var path in paths)
        {
            _files.Add(path);
        }

        return this;
    }

    /// <inheritdoc />
    public string? GetVariable(string name) => _variables.GetValueOrDefault(name);

    /// <inheritdoc />
    public bool FileExists(string path) => _files.Contains(path);

    /// <inheritdoc />
    public IReadOnlyList<string> GetDirectories(string directory, string pattern)
    {
        var separator = Platform == MergeToolPlatform.Windows ? '\\' : '/';
        var prefix = directory.TrimEnd(separator) + separator;
        var comparison = Platform == MergeToolPlatform.Linux ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        // A folder is the first part of the rest of a path that goes on below it.
        return _files
            .Where(file => file.StartsWith(prefix, comparison) && file[prefix.Length..].Contains(separator))
            .Select(file => file[prefix.Length..].Split(separator)[0])
            .Where(name => Matches(name, pattern))
            .Distinct()
            .Select(name => prefix + name)
            .ToList();
    }

    /// <summary>
    /// Tells whether a name matches a pattern with <c>*</c>.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="pattern">The pattern.</param>
    /// <returns><see langword="true"/> when it matches.</returns>
    private static bool Matches(string name, string pattern)
    {
        var parts = pattern.Split('*');
        if (!name.StartsWith(parts[0], StringComparison.OrdinalIgnoreCase) || !name.EndsWith(parts[^1], StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return name.Length >= parts[0].Length + parts[^1].Length;
    }
}
