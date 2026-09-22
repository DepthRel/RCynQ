using System.Text;
using RCynQ.Core.Abstractions.Patches;
using RCynQ.Core.Abstractions.Paths;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Patches;
using RCynQ.Localization;

namespace RCynQ.Sync.Content;

/// <summary>
/// <see cref="IPatchContent"/> that reads the incoming content straight from a working tree.
/// </summary>
/// <remarks>
/// Used by the sender: nothing is copied or held in memory, a file is opened when it is about to be sent. The reader does
/// not verify the hash; the receiver checks every file against the signed manifest, and the sync base is only updated from files
/// whose hash still matches.
/// </remarks>
public sealed class WorkdirPatchContent : IPatchContent
{
    /// <summary>
    /// The buffer size of the file streams.
    /// </summary>
    private const int BufferSize = 81920;

    /// <summary>
    /// The working tree.
    /// </summary>
    private readonly string _root;

    /// <summary>
    /// The path utility.
    /// </summary>
    private readonly IPathUtility _paths;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkdirPatchContent"/> class.
    /// </summary>
    /// <param name="root">The working tree.</param>
    /// <param name="paths">The path utility.</param>
    public WorkdirPatchContent(string root, IPathUtility paths)
    {
        _root = root;
        _paths = paths;
    }

    /// <inheritdoc />
    public Task<Stream> OpenReadAsync(FileChange change, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (change.Kind == ChangeKind.Deleted)
        {
            throw new ArgumentException("A deleted entry has no content.", nameof(change));
        }

        var full = _paths.ResolveWithinRoot(_root, change.Path);
        if (change.IsSymlink)
        {
            var target = new FileInfo(full).LinkTarget ?? new DirectoryInfo(full).LinkTarget
                ?? throw new SyncBridgeException(Texts.Format("Content.NotALink", change.Path));
            return Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(target)));
        }

        if (!File.Exists(full))
        {
            throw new SyncBridgeException(Texts.Format("Content.FileMissing", change.Path));
        }

        // The file may be open in an editor; sharing for writing keeps a running IDE from blocking the transfer.
        return Task.FromResult<Stream>(new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, BufferSize, useAsync: true));
    }
}
