using RCynQ.Core.Config;

namespace RCynQ.UI.Services.Reset;

/// <summary>
/// What a reset of the node did.
/// </summary>
/// <param name="Report">What was removed from this computer.</param>
/// <param name="AccessText">
/// What became of the lines of the node in <c>authorized_keys</c>, in the language of the interface, or
/// <see langword="null"/> when there were none.
/// </param>
public sealed record NodeResetOutcome(NodeResetReport Report, string? AccessText);
