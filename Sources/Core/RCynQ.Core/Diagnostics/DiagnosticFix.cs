using System.Text.Json.Serialization;
using RCynQ.Core.Validation;

namespace RCynQ.Core.Diagnostics;

/// <summary>
/// A proposed automatic fix attached to a failed diagnostic result.
/// </summary>
public sealed record DiagnosticFix
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DiagnosticFix"/> record.
    /// </summary>
    /// <param name="kind">The fix kind.</param>
    /// <param name="description">A human-readable description shown before confirmation.</param>
    /// <param name="parameters">Fix parameters, for example <c>path</c> or <c>linkId</c>.</param>
    /// <exception cref="ArgumentException">The kind is not defined or the description is blank.</exception>
    [JsonConstructor]
    public DiagnosticFix(DiagnosticFixKind kind, string description, IReadOnlyDictionary<string, string>? parameters = null)
    {
        Kind = Guard.Defined(kind, nameof(kind));
        Description = Guard.NotBlank(description, nameof(description));
        Parameters = parameters is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(parameters, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gets the fix kind.
    /// </summary>
    public DiagnosticFixKind Kind { get; }

    /// <summary>
    /// Gets the description shown before confirmation.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// Gets the fix parameters.
    /// </summary>
    public IReadOnlyDictionary<string, string> Parameters { get; }
}
