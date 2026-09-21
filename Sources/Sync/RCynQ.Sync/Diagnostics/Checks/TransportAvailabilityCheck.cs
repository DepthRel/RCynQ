using RCynQ.Core.Diagnostics;
using RCynQ.Core.Enums;
using RCynQ.Transports.Abstractions.Factory;
using RCynQ.Localization;

namespace RCynQ.Sync.Diagnostics.Checks;

/// <summary>
/// Checks that every configured transport is available and that tunnel transports are healthy.
/// </summary>
public sealed class TransportAvailabilityCheck : DiagnosticCheckBase
{
    /// <summary>The link factory.</summary>
    private readonly TransportLinkFactory _factory;

    /// <summary>
    /// Initializes a new instance of the <see cref="TransportAvailabilityCheck"/> class.
    /// </summary>
    /// <param name="factory">The link factory.</param>
    public TransportAvailabilityCheck(TransportLinkFactory factory)
        : base("TransportAvailability", AllRoles)
    {
        _factory = factory;
    }

    /// <inheritdoc />
    public override async Task<DiagnosticResult> RunAsync(DiagnosticContext context, CancellationToken cancellationToken = default)
    {
        var problems = new List<string>();
        DiagnosticFix? fix = null;
        foreach (var link in context.Chain)
        {
            foreach (var kind in new[] { link.Transport }.Concat(link.FallbackTransports))
            {
                if (!_factory.IsSupported(kind))
                {
                    problems.Add($"{link.Name}: transport {kind} is not available in this build");
                }
            }

            if (link.Transport != TransportKind.Direct && _factory.IsSupported(link.Transport))
            {
                var result = await _factory.Create(link).DiagnoseAsync(cancellationToken);
                if (!result.Passed)
                {
                    problems.Add($"{link.Name}: {result.Message}");
                    fix ??= result.AutoFix;
                }
            }
        }

        return problems.Count == 0
            ? Pass(Texts.Get("Diag.Transports.Ok"))
            : Fail(string.Join("; ", problems), Texts.Get("Diag.Transports.Fix"), fix);
    }
}
