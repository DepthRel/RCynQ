using System.Text.RegularExpressions;
using RCynQ.Core.Abstractions;
using RCynQ.Core.Config;
using RCynQ.Core.Diagnostics;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.State;
using RCynQ.Git.Commands;
using RCynQ.Ssh.Keys;
using RCynQ.Sync.Security;
using RCynQ.Transports.Abstractions;

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
            return Task.FromResult(Fail("No working directory is configured."));
        }

        if (!Directory.Exists(workdir))
        {
            return Task.FromResult(Fail(
                $"Working directory '{workdir}' does not exist.",
                "Create the directory or clone the repository into it.",
                new DiagnosticFix(DiagnosticFixKind.CreateDirectory, $"Create '{workdir}'.", new Dictionary<string, string> { ["path"] = workdir })));
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
            return Task.FromResult(Fail($"'{workdir}' is not writable: {ex.Message}", "Grant the RCynQ user write access to the working directory."));
        }
    }
}
