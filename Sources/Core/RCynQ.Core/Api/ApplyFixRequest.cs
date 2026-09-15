using RCynQ.Core.Diagnostics;

namespace RCynQ.Core.Api;

/// <summary>
/// Body of <c>POST /diagnostics/fix</c>.
/// </summary>
/// <param name="Fix">The proposed fix taken from a diagnostic result.</param>
/// <param name="Confirmed">Whether the user explicitly confirmed the fix.</param>
public sealed record ApplyFixRequest(DiagnosticFix Fix, bool Confirmed);
