using RCynQ.Core.Enums;

namespace RCynQ.UI.Services.Configuration;

/// <summary>
/// The values entered in the first-run setup.
/// </summary>
/// <param name="NodeId">The node identifier.</param>
/// <param name="Name">The display name.</param>
/// <param name="Role">The role.</param>
/// <param name="Path">The working copy (Source) or repository (Dest); ignored for a Mediator.</param>
/// <param name="ListenPort">The service port.</param>
/// <param name="LinkToken">The shared link token, or an empty string.</param>
public sealed record NodeSetupValues(string NodeId, string Name, NodeRole Role, string Path, int ListenPort, string LinkToken);
