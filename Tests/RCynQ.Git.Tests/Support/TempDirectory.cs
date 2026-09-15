namespace RCynQ.Git.Tests.Support;

/// <summary>
/// A temporary directory deleted on dispose.
/// </summary>
public sealed class TempDirectory : IDisposable
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TempDirectory"/> class.
    /// </summary>
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rcynq-git-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    /// <summary>Gets the directory path.</summary>
    public string Path { get; }

    /// <summary>
    /// Combines a relative path with the directory.
    /// </summary>
    /// <param name="relative">The relative path with <c>/</c> separators.</param>
    /// <returns>The full path.</returns>
    public string this[string relative] => System.IO.Path.Combine(Path, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));

    /// <summary>
    /// Writes a text file, creating parent directories.
    /// </summary>
    /// <param name="relative">The relative path.</param>
    /// <param name="content">The content.</param>
    /// <returns>The full path.</returns>
    public string Write(string relative, string content)
    {
        var full = this[relative];
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    /// <summary>
    /// Deletes the directory.
    /// </summary>
    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // Best effort cleanup.
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort cleanup.
        }
    }
}
