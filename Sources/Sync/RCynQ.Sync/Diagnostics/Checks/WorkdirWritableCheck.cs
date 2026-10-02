using RCynQ.Core.Diagnostics;
using RCynQ.Localization;

namespace RCynQ.Sync.Diagnostics.Checks;

/// <summary>
/// Checks that the working tree exists and is writable.
/// </summary>
public sealed class WorkdirWritableCheck : DiagnosticCheckBase
{
    /// <summary>The localizer of the messages.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkdirWritableCheck"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    public WorkdirWritableCheck(ILocalizer localizer)
        : base("WorkdirWritable", PatchRoles)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
    }

    /// <inheritdoc />
    public override Task<DiagnosticResult> RunAsync(DiagnosticContext context, CancellationToken cancellationToken = default)
    {
        var workdir = context.Node.EffectiveWorkdir;
        if (workdir is null)
        {
            return Task.FromResult(Fail(_localizer.Get("Diag.Workdir.None")));
        }

        if (!Directory.Exists(workdir))
        {
            return Task.FromResult(Fail(
                _localizer.Format("Diag.Workdir.Missing", workdir),
                _localizer.Get("Diag.Workdir.MissingFix"),
                new DiagnosticFix(DiagnosticFixKind.CreateDirectory, _localizer.Format("Diag.Workdir.Create", workdir), new Dictionary<string, string> { ["path"] = workdir })));
        }

        var probe = Path.Combine(workdir, $".rcynq-write-test-{Guid.NewGuid():N}");
        try
        {
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return Task.FromResult(Pass(_localizer.Format("Diag.Workdir.Ok", workdir)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(Fail(_localizer.Format("Diag.Workdir.NotWritable", workdir, _localizer.Describe(ex)), _localizer.Get("Diag.Workdir.NotWritableFix")));
        }
    }
}
