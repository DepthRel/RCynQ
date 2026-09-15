using System.Text.Json.Serialization;
using RCynQ.Core.Validation;

namespace RCynQ.Core.Diagnostics;

/// <summary>
/// The result of a single diagnostic check.
/// </summary>
public sealed record DiagnosticResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DiagnosticResult"/> record.
    /// </summary>
    /// <param name="checkName">The stable check name.</param>
    /// <param name="passed">Whether the check passed.</param>
    /// <param name="message">A human-readable outcome description.</param>
    /// <param name="suggestedFix">A suggested manual remediation, or <see langword="null"/>.</param>
    /// <param name="autoFix">A safe automatic fix the user may confirm, or <see langword="null"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="checkName"/> is blank.</exception>
    [JsonConstructor]
    public DiagnosticResult(string checkName, bool passed, string? message, string? suggestedFix, DiagnosticFix? autoFix = null)
    {
        CheckName = Guard.NotBlank(checkName, nameof(checkName));
        Passed = passed;
        Message = message;
        SuggestedFix = suggestedFix;
        AutoFix = autoFix;
    }

    /// <summary>
    /// Gets the stable check name.
    /// </summary>
    public string CheckName { get; }

    /// <summary>
    /// Gets a value indicating whether the check passed.
    /// </summary>
    public bool Passed { get; }

    /// <summary>
    /// Gets a human-readable outcome description.
    /// </summary>
    public string? Message { get; }

    /// <summary>
    /// Gets a suggested manual remediation.
    /// </summary>
    public string? SuggestedFix { get; }

    /// <summary>
    /// Gets a safe automatic fix, or <see langword="null"/>.
    /// </summary>
    public DiagnosticFix? AutoFix { get; }

    /// <summary>
    /// Creates a passed result.
    /// </summary>
    /// <param name="checkName">The check name.</param>
    /// <param name="message">The message.</param>
    /// <returns>The result.</returns>
    public static DiagnosticResult Pass(string checkName, string? message = null) => new(checkName, true, message, null);

    /// <summary>
    /// Creates a failed result.
    /// </summary>
    /// <param name="checkName">The check name.</param>
    /// <param name="message">The message.</param>
    /// <param name="suggestedFix">The suggested fix.</param>
    /// <param name="autoFix">An optional safe automatic fix.</param>
    /// <returns>The result.</returns>
    public static DiagnosticResult Fail(string checkName, string message, string? suggestedFix = null, DiagnosticFix? autoFix = null)
        => new(checkName, false, message, suggestedFix, autoFix);
}
