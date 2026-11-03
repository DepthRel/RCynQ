using System.Text;
using RCynQ.Core.Diagnostics;
using RCynQ.Localization;

namespace RCynQ.Cli.Services;

/// <summary>
/// Runs diagnostics, prints results with suggested fixes and applies confirmed safe fixes.
/// </summary>
public sealed class DiagnosticsCommandService
{
    /// <summary>The diagnostics runner.</summary>
    private readonly IDiagnosticsRunner _runner;

    /// <summary>The auto-fixer.</summary>
    private readonly IAutoFixer _fixer;

    /// <summary>The localizer of the output.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="DiagnosticsCommandService"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the output.</param>
    /// <param name="runner">The diagnostics runner.</param>
    /// <param name="fixer">The auto-fixer.</param>
    public DiagnosticsCommandService(ILocalizer localizer, IDiagnosticsRunner runner, IAutoFixer fixer)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _runner = runner;
        _fixer = fixer;
    }

    /// <summary>
    /// Runs diagnostics.
    /// </summary>
    /// <param name="applyFixes">Whether proposed safe fixes should be applied.</param>
    /// <param name="confirmed">Whether the user confirmed the fixes (<c>--yes</c>).</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The report and whether every check passed.</returns>
    public async Task<(string Text, bool AllPassed)> RunAsync(bool applyFixes, bool confirmed, CancellationToken cancellationToken = default)
    {
        var run = await _runner.RunAsync(cancellationToken);
        var width = run.Results.Select(result => result.CheckName.Length).DefaultIfEmpty(0).Max();
        var text = new StringBuilder(_localizer.Format("Cli.Diagnostics.Title", run.Role)).Append('\n');
        foreach (var result in run.Results)
        {
            text.Append(result.Passed ? "[ OK ] " : "[FAIL] ").Append(result.CheckName.PadRight(width)).Append("  ").Append(result.Message).Append('\n');
            if (result.Passed)
            {
                continue;
            }

            if (result.SuggestedFix is not null)
            {
                text.Append("       ").Append(_localizer.Format("Cli.Diagnostics.Fix", result.SuggestedFix)).Append('\n');
            }

            if (result.AutoFix is not { } fix)
            {
                continue;
            }

            if (!applyFixes)
            {
                text.Append("       ").Append(_localizer.Format("Cli.Diagnostics.FixAvailable", fix.Description)).Append('\n');
                continue;
            }

            var outcome = await _fixer.ApplyAsync(fix, confirmed, cancellationToken);
            text.Append("       ").Append(outcome.Applied ? _localizer.Format("Cli.Diagnostics.FixApplied", outcome.Message) : _localizer.Format("Cli.Diagnostics.FixNotApplied", outcome.Message)).Append('\n');
        }

        var failed = run.Results.Count(result => !result.Passed);
        text.Append(_localizer.Format("Cli.Diagnostics.Summary", run.Results.Count - failed, failed)).Append('\n');
        return (text.ToString(), failed == 0);
    }
}
