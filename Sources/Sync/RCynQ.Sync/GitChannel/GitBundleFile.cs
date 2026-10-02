namespace RCynQ.Sync.GitChannel;

/// <summary>
/// A bundle file that is deleted when it is no longer needed.
/// </summary>
public sealed class GitBundleFile : IAsyncDisposable
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GitBundleFile"/> class.
    /// </summary>
    /// <param name="path">The bundle file.</param>
    /// <param name="head">The branch the served repository has checked out, or <see langword="null"/> for a detached HEAD.</param>
    public GitBundleFile(string path, string? head)
    {
        Path = path;
        Head = head;
    }

    /// <summary>
    /// Gets the bundle file.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Gets the branch the served repository has checked out, or <see langword="null"/> for a detached HEAD.
    /// </summary>
    public string? Head { get; }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        try
        {
            File.Delete(Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Temporary data of the node; the next cleanup removes it.
        }

        return ValueTask.CompletedTask;
    }
}
