namespace RCynQ.Core.Api;

/// <summary>
/// What became of the lines this node wrote into the <c>authorized_keys</c> file of the local SSH server for its
/// neighbours and its invitations.
/// </summary>
/// <param name="FilePath">The <c>authorized_keys</c> file.</param>
/// <param name="Removed">The number of lines removed.</param>
/// <param name="NeedsElevation">
/// Whether lines of this node stay in the file because only an administrator may change it (the Windows file of the
/// Administrators group).
/// </param>
public sealed record AccessRevocationResult(string FilePath, int Removed, bool NeedsElevation);
