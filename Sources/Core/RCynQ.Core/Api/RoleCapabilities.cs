using RCynQ.Core.Enums;

namespace RCynQ.Core.Api;

/// <summary>
/// What a node role is allowed to do. The service enforces it and the UI adapts its tabs to it.
/// </summary>
/// <param name="SendsPatches">Whether the role builds and sends live patches.</param>
/// <param name="ReceivesPatches">Whether the role accepts live patches.</param>
/// <param name="ServesGit">Whether the role provides git-over-ssh (Dest).</param>
/// <param name="FetchesGit">Whether the role fetches branches through Dest (Source).</param>
/// <param name="ManagesLinks">Whether the role maintains transport links.</param>
public sealed record RoleCapabilities(bool SendsPatches, bool ReceivesPatches, bool ServesGit, bool FetchesGit, bool ManagesLinks)
{
    /// <summary>
    /// Gets a value indicating whether the synchronization tab applies.
    /// </summary>
    public bool ShowsSyncTab => SendsPatches || ReceivesPatches;

    /// <summary>
    /// Gets a value indicating whether the branches tab applies.
    /// </summary>
    public bool ShowsBranchesTab => ServesGit || FetchesGit;

    /// <summary>
    /// Gets the capabilities of a role.
    /// </summary>
    /// <param name="role">The role.</param>
    /// <returns>The capabilities.</returns>
    /// <exception cref="ArgumentException"><paramref name="role"/> is not defined.</exception>
    public static RoleCapabilities For(NodeRole role) => role switch
    {
        NodeRole.Source => new RoleCapabilities(true, true, false, true, true),
        NodeRole.Dest => new RoleCapabilities(true, true, true, false, true),
        NodeRole.Mediator => new RoleCapabilities(false, false, false, false, true),
        _ => throw new ArgumentException($"Node role '{role}' is not defined.", nameof(role))
    };
}
