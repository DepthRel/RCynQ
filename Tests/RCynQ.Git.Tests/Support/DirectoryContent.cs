using System.Text;
using RCynQ.Core.Abstractions.Patches;
using RCynQ.Core.Patches;

namespace RCynQ.Git.Tests.Support;

/// <summary>
/// An <see cref="IPatchContent"/> that reads the incoming content from a directory of the test.
/// </summary>
public sealed class DirectoryContent : IPatchContent
{
    /// <summary>
    /// The directory.
    /// </summary>
    private readonly string _root;

    /// <summary>
    /// Initializes a new instance of the <see cref="DirectoryContent"/> class.
    /// </summary>
    /// <param name="root">The directory.</param>
    public DirectoryContent(string root)
    {
        _root = root;
    }

    /// <inheritdoc />
    public Task<Stream> OpenReadAsync(FileChange change, CancellationToken cancellationToken = default)
    {
        var full = Path.Combine(_root, change.Path.Replace('/', Path.DirectorySeparatorChar));
        var link = new FileInfo(full).LinkTarget;
        return Task.FromResult<Stream>(link is not null ? new MemoryStream(Encoding.UTF8.GetBytes(link)) : File.OpenRead(full));
    }
}
