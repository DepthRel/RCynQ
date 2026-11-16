using RCynQ.Core.Enums;

namespace RCynQ.Core.Api;

/// <summary>
/// Asks the Workstation to switch its working tree to another branch.
/// </summary>
/// <param name="Branch">The branch: a local one, or one fetched from the Origin that becomes local.</param>
/// <param name="Mode">What happens to uncommitted changes.</param>
/// <param name="Patch">
/// With <see cref="BranchSwitchMode.SavePatch"/>, the patch of the changes the user has saved to a file
/// (as <see cref="ApiRoutes.GIT_SWITCH_PATCH"/> built it); it is taken out of the working tree before the switch.
/// </param>
public sealed record SwitchBranchRequest(string Branch, BranchSwitchMode Mode, byte[]? Patch = null);
