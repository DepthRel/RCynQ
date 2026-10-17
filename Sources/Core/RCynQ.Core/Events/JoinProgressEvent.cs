using RCynQ.Core.Enums;

namespace RCynQ.Core.Events;

/// <summary>
/// A stage of joining with an invitation has started or ended; the application shows it while joining runs.
/// </summary>
/// <param name="Step">The stage.</param>
/// <param name="State">Whether it started, succeeded or failed.</param>
/// <param name="Message">The localized message of an ended stage, or <see langword="null"/> for a started one.</param>
/// <param name="Inviter">The role of the inviting node once the invitation is read, or <see langword="null"/> before.</param>
public sealed record JoinProgressEvent(JoinStep Step, JoinStepState State, string? Message = null, NodeRole? Inviter = null);
