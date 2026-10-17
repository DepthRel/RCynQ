namespace RCynQ.UI.Enums;

/// <summary>
/// A page of the connect wizard, in the order they come.
/// </summary>
public enum WizardStep
{
    /// <summary>
    /// Directly or through a Relay.
    /// </summary>
    Scenario = 0,

    /// <summary>
    /// Whether this computer accepts neighbours, with the buttons that make it do so.
    /// </summary>
    Readiness = 1,

    /// <summary>
    /// What this node does now: join with an invitation or invite a node.
    /// </summary>
    Action = 2,

    /// <summary>
    /// Creating an invitation and waiting until the invited node joins.
    /// </summary>
    Invite = 3,

    /// <summary>
    /// Joining with an invitation.
    /// </summary>
    Join = 4,

    /// <summary>
    /// The chain is set up.
    /// </summary>
    Done = 5
}
