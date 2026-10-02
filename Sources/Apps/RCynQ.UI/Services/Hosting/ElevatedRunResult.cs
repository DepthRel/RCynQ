using RCynQ.UI.Enums;

namespace RCynQ.UI.Services.Hosting;

/// <summary>
/// The result of running a command with administrator rights.
/// </summary>
/// <param name="Outcome">The outcome.</param>
/// <param name="Message">A user-facing description.</param>
public sealed record ElevatedRunResult(ElevatedRunOutcome Outcome, string Message);
