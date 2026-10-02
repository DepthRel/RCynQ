using RCynQ.Core.Diagnostics;
using RCynQ.Core.Enums;
using RCynQ.Localization;
using RCynQ.Transports.Abstractions.Factory;

namespace RCynQ.Sync.Diagnostics.Checks;

/// <summary>
/// Checks that every configured transport is available and that tunnel transports are healthy.
/// </summary>
public sealed class TransportAvailabilityCheck : DiagnosticCheckBase
{
    /// <summary>The link factory.</summary>
    private readonly TransportLinkFactory _factory;

    /// <summary>The localizer of the messages.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="TransportAvailabilityCheck"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="factory">The link factory.</param>
    public TransportAvailabilityCheck(ILocalizer localizer, TransportLinkFactory factory)
        : base("TransportAvailability", _allRoles)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
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
                    problems.Add(_localizer.Format("Diag.Transports.NotInBuild", link.Name, kind));
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
            ? Pass(_localizer.Get("Diag.Transports.Ok"))
            : Fail(string.Join("; ", problems), _localizer.Get("Diag.Transports.Fix"), fix);
    }
}
