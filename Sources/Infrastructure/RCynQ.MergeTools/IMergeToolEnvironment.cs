using RCynQ.MergeTools.Enums;

namespace RCynQ.MergeTools;

/// <summary>
/// The parts of the machine the search for merge tools looks at; tests replace them.
/// </summary>
public interface IMergeToolEnvironment
{
    /// <summary>
    /// Gets the operating system.
    /// </summary>
    MergeToolPlatform Platform { get; }

    /// <summary>
    /// Gets the home directory of the user.
    /// </summary>
    string HomeDirectory { get; }

    /// <summary>
    /// Reads an environment variable.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <returns>The value, or <see langword="null"/> when it is not set.</returns>
    string? GetVariable(string name);

    /// <summary>
    /// Tells whether a file exists.
    /// </summary>
    /// <param name="path">The full path.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    bool FileExists(string path);

    /// <summary>
    /// Lists the directories directly below a directory whose names match a pattern with <c>*</c>.
    /// </summary>
    /// <param name="directory">The directory.</param>
    /// <param name="pattern">The pattern.</param>
    /// <returns>The full paths; nothing when the directory is missing or cannot be read.</returns>
    IReadOnlyList<string> GetDirectories(string directory, string pattern);
}
