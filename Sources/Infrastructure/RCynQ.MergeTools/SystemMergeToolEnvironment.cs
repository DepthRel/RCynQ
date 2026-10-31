using RCynQ.MergeTools.Enums;

namespace RCynQ.MergeTools;

/// <summary>
/// <see cref="IMergeToolEnvironment"/> of the machine the program runs on.
/// </summary>
public sealed class SystemMergeToolEnvironment : IMergeToolEnvironment
{
    /// <inheritdoc />
    public MergeToolPlatform Platform => OperatingSystem.IsWindows()
        ? MergeToolPlatform.Windows
        : OperatingSystem.IsMacOS() ? MergeToolPlatform.MacOS : MergeToolPlatform.Linux;

    /// <inheritdoc />
    public string HomeDirectory => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <inheritdoc />
    public string? GetVariable(string name) => Environment.GetEnvironmentVariable(name);

    /// <inheritdoc />
    public bool FileExists(string path) => File.Exists(path);

    /// <inheritdoc />
    public IReadOnlyList<string> GetDirectories(string directory, string pattern)
    {
        try
        {
            return Directory.Exists(directory) ? Directory.GetDirectories(directory, pattern) : Array.Empty<string>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }
}
