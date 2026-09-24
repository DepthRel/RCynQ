using RCynQ.Core.Enums;

namespace RCynQ.Core.State;

/// <summary>
/// A file of the working tree that differs from the commit both nodes are built on, as the Synchronization tab lists it.
/// </summary>
/// <param name="Kind">How the file differs.</param>
/// <param name="Path">The path relative to the working tree, with <c>/</c> separators.</param>
/// <param name="AlreadySent">
/// <see langword="true"/> when the peer already has this version of the file, so it is not among the pending changes; the file
/// stays in the list until the change is committed, and can still be sent again.
/// </param>
public sealed record ChangedFile(ChangeKind Kind, string Path, bool AlreadySent);
