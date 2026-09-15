using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RCynQ.Core.Abstractions;
using RCynQ.Core.Config;
using RCynQ.Core.Diagnostics;
using RCynQ.Core.Enums;
using RCynQ.Core.Events;
using RCynQ.Core.Exceptions;
using RCynQ.Core.State;
using RCynQ.Ssh.Keys;
using RCynQ.Transports.Abstractions;

namespace RCynQ.Sync.Diagnostics.AutoFix;

/// <summary>
/// <see cref="IAutoFixer"/> that only executes an allow-list of non-destructive fixes after explicit confirmation.
/// </summary>
/// <remarks>
/// Allowed: create a missing directory (only below the working tree or the application data root), start a transport,
/// add a restricted <c>authorized_keys</c> line, restart the local endpoint. Global SSH configuration changes, deleting
/// user data and silent system service changes are always refused.
/// </remarks>
public sealed class SafeAutoFixer : IAutoFixer
{
    /// <summary>
    /// The fix kinds that may be executed.
    /// </summary>
    public static readonly IReadOnlySet<DiagnosticFixKind> AllowedKinds = new HashSet<DiagnosticFixKind>
    {
        DiagnosticFixKind.CreateDirectory,
        DiagnosticFixKind.StartTransport,
        DiagnosticFixKind.AddRestrictedAuthorizedKey,
        DiagnosticFixKind.RestartLocalEndpoint
    };

    /// <summary>The handlers by kind.</summary>
    private readonly IReadOnlyDictionary<DiagnosticFixKind, IAutoFixHandler> _handlers;

    /// <summary>
    /// Initializes a new instance of the <see cref="SafeAutoFixer"/> class.
    /// </summary>
    /// <param name="handlers">The handlers; handlers for kinds outside the allow-list are ignored.</param>
    public SafeAutoFixer(IEnumerable<IAutoFixHandler> handlers)
    {
        _handlers = handlers.Where(handler => AllowedKinds.Contains(handler.Kind)).ToDictionary(handler => handler.Kind);
    }

    /// <inheritdoc />
    public bool IsAllowed(DiagnosticFixKind kind) => AllowedKinds.Contains(kind);

    /// <inheritdoc />
    public async Task<AutoFixResult> ApplyAsync(DiagnosticFix fix, bool confirmed, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fix);
        if (!IsAllowed(fix.Kind))
        {
            return new AutoFixResult(false, $"Fix '{fix.Kind}' is not allowed automatically; apply it manually after review.");
        }

        if (!confirmed)
        {
            return new AutoFixResult(false, $"Fix '{fix.Description}' requires explicit confirmation.");
        }

        if (!_handlers.TryGetValue(fix.Kind, out var handler))
        {
            return new AutoFixResult(false, $"No handler is registered for fix '{fix.Kind}' in this process.");
        }

        try
        {
            return await handler.ExecuteAsync(fix, cancellationToken);
        }
        catch (Exception ex) when (ex is SyncBridgeException or ArgumentException or IOException or UnauthorizedAccessException or KeyNotFoundException or FormatException)
        {
            return new AutoFixResult(false, $"Fix '{fix.Description}' failed: {ex.Message}");
        }
    }
}
