using RCynQ.Core.Diagnostics;
using RCynQ.Localization;

namespace RCynQ.Sync.Diagnostics.Checks;

/// <summary>
/// Checks that the working tree exists and is writable.
/// </summary>
public sealed class WorkdirWritableCheck : DiagnosticCheckBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WorkdirWritableCheck"/> class.
    /// </summary>
    public WorkdirWritableCheck()
        : base("WorkdirWritable", PatchRoles)
    {
    }

    /// <inheritdoc />
    public override Task<DiagnosticResult> RunAsync(DiagnosticContext context, CancellationToken cancellationToken = default)
    {
        var workdir = context.Node.EffectiveWorkdir;
        if (workdir is null)
        {
            return Task.FromResult(Fail(Texts.Get("Diag.Workdir.None")));
        }

        if (!Directory.Exists(workdir))
        {
            return Task.FromResult(Fail(
                Texts.Format("Diag.Workdir.Missing", workdir),
                Texts.Get("Diag.Workdir.MissingFix"),
                new DiagnosticFix(DiagnosticFixKind.CreateDirectory, Texts.Format("Diag.Workdir.Create", workdir), new Dictionary<string, string> { ["path"] = workdir })));
        }

        var probe = Path.Combine(workdir, $".rcynq-write-test-{Guid.NewGuid():N}");
        try
        {
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return Task.FromResult(Pass($"'{workdir}' is writable."));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(Fail(Texts.Format("Diag.Workdir.NotWritable", workdir, ex.Message), Texts.Get("Diag.Workdir.NotWritableFix")));
        }
    }
}
