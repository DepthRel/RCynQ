using RCynQ.Core.Diagnostics;

namespace RCynQ.UI.ViewModels.Items;

/// <summary>
/// A check result on the "Диагностика" tab.
/// </summary>
public sealed class DiagnosticItemViewModel
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DiagnosticItemViewModel"/> class.
    /// </summary>
    /// <param name="result">The result.</param>
    public DiagnosticItemViewModel(DiagnosticResult result)
    {
        Result = result ?? throw new ArgumentNullException(nameof(result));
    }

    /// <summary>Gets the result.</summary>
    public DiagnosticResult Result { get; }

    /// <summary>Gets the check name.</summary>
    public string Name => Result.CheckName;

    /// <summary>Gets a value indicating whether the check passed.</summary>
    public bool Passed => Result.Passed;

    /// <summary>Gets the result mark.</summary>
    public string Mark => Result.Passed ? "✓" : "✗";

    /// <summary>Gets the message.</summary>
    public string? Message => Result.Message;

    /// <summary>Gets the suggested manual fix.</summary>
    public string? SuggestedFix => Result.SuggestedFix;

    /// <summary>Gets a value indicating whether a suggested fix is shown.</summary>
    public bool HasSuggestedFix => !Result.Passed && !string.IsNullOrEmpty(Result.SuggestedFix);

    /// <summary>Gets a value indicating whether an automatic fix is available.</summary>
    public bool CanFix => !Result.Passed && Result.AutoFix is not null;
}
