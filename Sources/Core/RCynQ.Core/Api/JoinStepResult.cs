using RCynQ.Core.Enums;

namespace RCynQ.Core.Api;

/// <summary>
/// How one stage of joining ended.
/// </summary>
/// <param name="Step">The stage.</param>
/// <param name="Succeeded">Whether it succeeded.</param>
/// <param name="Message">The localized message of the stage.</param>
public sealed record JoinStepResult(JoinStep Step, bool Succeeded, string Message);
