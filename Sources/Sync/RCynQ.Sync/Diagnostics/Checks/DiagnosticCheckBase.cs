using RCynQ.Core.Diagnostics;
using RCynQ.Core.Enums;

namespace RCynQ.Sync.Diagnostics.Checks;

/// <summary>
/// Base class for diagnostic checks with a fixed name and role set.
/// </summary>
public abstract class DiagnosticCheckBase : IDiagnosticCheck
{
    /// <summary>
    /// All roles.
    /// </summary>
    protected static readonly NodeRole[] _allRoles = { NodeRole.Workstation, NodeRole.Relay, NodeRole.Origin };

    /// <summary>
    /// The patch channel roles.
    /// </summary>
    protected static readonly NodeRole[] _patchRoles = { NodeRole.Workstation, NodeRole.Origin };

    /// <summary>
    /// Initializes a new instance of the <see cref="DiagnosticCheckBase"/> class.
    /// </summary>
    /// <param name="name">The check name.</param>
    /// <param name="roles">The applicable roles.</param>
    protected DiagnosticCheckBase(string name, IEnumerable<NodeRole> roles)
    {
        Name = name;
        Roles = roles.ToHashSet();
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public IReadOnlySet<NodeRole> Roles { get; }

    /// <inheritdoc />
    public abstract Task<DiagnosticResult> RunAsync(DiagnosticContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a passed result.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <returns>The result.</returns>
    protected DiagnosticResult Pass(string? message = null) => DiagnosticResult.Pass(Name, message);

    /// <summary>
    /// Creates a failed result.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="suggestedFix">The suggested manual fix.</param>
    /// <param name="autoFix">The proposed safe automatic fix.</param>
    /// <returns>The result.</returns>
    protected DiagnosticResult Fail(string message, string? suggestedFix = null, DiagnosticFix? autoFix = null)
        => DiagnosticResult.Fail(Name, message, suggestedFix, autoFix);
}
