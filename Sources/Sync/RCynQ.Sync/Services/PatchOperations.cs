using RCynQ.Core.Config;
using RCynQ.Core.Exceptions;
using RCynQ.Localization;

namespace RCynQ.Sync.Services;

/// <summary>
/// What the operations of the patch channel have in common.
/// </summary>
internal static class PatchOperations
{
    /// <summary>
    /// Throws for nodes outside the patch channel.
    /// </summary>
    /// <param name="node">The local node.</param>
    /// <param name="localizer">The localizer of the message.</param>
    /// <exception cref="SyncBridgeException">The node is a Relay.</exception>
    public static void EnsureParticipant(NodeConfig node, ILocalizer localizer)
    {
        if (!node.ParticipatesInPatchChannel)
        {
            throw new SyncBridgeException(localizer.Format("Patch.NoChannel", node.Role));
        }
    }

    /// <summary>
    /// Refreshes the stored state after an operation that already succeeded, so a failing refresh does not report the operation as failed.
    /// </summary>
    /// <param name="refresh">The refresh.</param>
    /// <returns>A task that completes when the refresh is done or has failed.</returns>
    public static async Task RefreshQuietlyAsync(Func<Task> refresh)
    {
        try
        {
            await refresh();
        }
        catch (Exception ex) when (ex is SyncBridgeException or IOException or UnauthorizedAccessException)
        {
            // The next status request refreshes the state again.
        }
    }
}
