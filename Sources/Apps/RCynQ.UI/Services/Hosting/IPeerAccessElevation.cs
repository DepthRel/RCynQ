namespace RCynQ.UI.Services.Hosting;

/// <summary>
/// Allows the neighbours in the SSH server settings with administrator rights, which the UI itself does not have.
/// </summary>
public interface IPeerAccessElevation
{
    /// <summary>
    /// Gets a value indicating whether elevation can be requested on this computer.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Runs <c>rcynq ssh allow</c> with administrator rights; Windows asks the user to confirm.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>The result.</returns>
    Task<ElevatedRunResult> AllowPeerAccessAsync(CancellationToken cancellationToken = default);
}
