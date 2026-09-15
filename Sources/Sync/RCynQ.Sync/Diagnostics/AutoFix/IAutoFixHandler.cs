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
/// Executes one kind of safe automatic fix.
/// </summary>
public interface IAutoFixHandler
{
    /// <summary>
    /// Gets the handled fix kind.
    /// </summary>
    DiagnosticFixKind Kind { get; }

    /// <summary>
    /// Executes the fix.
    /// </summary>
    /// <param name="fix">The fix.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The outcome.</returns>
    Task<AutoFixResult> ExecuteAsync(DiagnosticFix fix, CancellationToken cancellationToken);
}
