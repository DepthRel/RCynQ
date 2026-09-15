using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Events;
using RCynQ.Core.State;

namespace RCynQ.Core.Diagnostics;

/// <summary>
/// The outcome of an automatic fix attempt.
/// </summary>
/// <param name="Applied">Whether the fix was executed successfully.</param>
/// <param name="Message">A human-readable explanation.</param>
public sealed record AutoFixResult(bool Applied, string Message);
