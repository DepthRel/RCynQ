using RCynQ.Core.Exceptions;
using RCynQ.Core.Patches;

namespace RCynQ.Core.Abstractions.Patches;

/// <summary>
/// Gives read access to the incoming content of the files of one patch.
/// </summary>
/// <remarks>
/// On the sender the content is the working tree; on the receiver it is the content the peer uploaded and the receiver
/// verified against the signed manifest. Content is always read as a stream so that a large file is never held in memory.
/// </remarks>
public interface IPatchContent
{
    /// <summary>
    /// Opens the incoming content of an added or modified entry.
    /// </summary>
    /// <param name="change">The manifest entry; it must not be a deletion.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// A readable stream with the file bytes, or with the UTF-8 text of the link target when the entry is a symbolic link.
    /// The caller disposes the stream.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="change"/> is <see langword="null"/>.</exception>
    /// <exception cref="SyncBridgeException">The content is not available.</exception>
    Task<Stream> OpenReadAsync(FileChange change, CancellationToken cancellationToken = default);
}
