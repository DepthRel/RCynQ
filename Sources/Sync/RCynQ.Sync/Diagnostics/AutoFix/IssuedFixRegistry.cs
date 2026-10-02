using System.Text;
using RCynQ.Core.Diagnostics;

namespace RCynQ.Sync.Diagnostics.AutoFix;

/// <summary>
/// Remembers the fixes the node proposed in its recent diagnostics, so a request to apply a fix can be checked against
/// them.
/// </summary>
/// <remarks>
/// A fix travels to the client with the result of a check and comes back when the user confirms it. Applying whatever
/// comes back would let a client choose the parameters itself: which key is added to <c>authorized_keys</c>, which
/// directory is created. With this registry the node applies only a fix that is, value for value, one it proposed. The
/// registry lives as long as the process: after a restart the diagnostics are run again before a fix can be applied.
/// </remarks>
public sealed class IssuedFixRegistry
{
    /// <summary>
    /// The number of proposed fixes that are remembered; older ones are forgotten first.
    /// </summary>
    public const int CAPACITY = 256;

    /// <summary>Guards the collections.</summary>
    private readonly object _gate = new();

    /// <summary>The signatures of the remembered fixes.</summary>
    private readonly HashSet<string> _issued = new(StringComparer.Ordinal);

    /// <summary>The signatures in the order they were remembered.</summary>
    private readonly Queue<string> _order = new();

    /// <summary>
    /// Remembers the fixes proposed by the results of a diagnostics run.
    /// </summary>
    /// <param name="results">The results.</param>
    public void Remember(IEnumerable<DiagnosticResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        lock (_gate)
        {
            foreach (var fix in results.Select(result => result.AutoFix).OfType<DiagnosticFix>())
            {
                var signature = Signature(fix);
                if (!_issued.Add(signature))
                {
                    continue;
                }

                _order.Enqueue(signature);
                while (_order.Count > CAPACITY)
                {
                    _issued.Remove(_order.Dequeue());
                }
            }
        }
    }

    /// <summary>
    /// Tells whether a fix is one the node proposed: the same kind with exactly the same parameters.
    /// </summary>
    /// <param name="fix">The fix a client asks to apply.</param>
    /// <returns><see langword="true"/> when the node proposed this fix.</returns>
    public bool IsIssued(DiagnosticFix fix)
    {
        ArgumentNullException.ThrowIfNull(fix);
        lock (_gate)
        {
            return _issued.Contains(Signature(fix));
        }
    }

    /// <summary>
    /// Builds the text that identifies a fix: its kind and its parameters in a fixed order. The description is not part
    /// of it, because it is worded in the language of the request.
    /// </summary>
    /// <param name="fix">The fix.</param>
    /// <returns>The signature.</returns>
    private static string Signature(DiagnosticFix fix)
    {
        var builder = new StringBuilder().Append((int)fix.Kind);
        foreach (var (key, value) in fix.Parameters.OrderBy(pair => pair.Key.ToUpperInvariant(), StringComparer.Ordinal))
        {
            // Length-prefixed, so no choice of names and values can imitate another set of parameters.
            var name = key.ToUpperInvariant();
            builder.Append('|').Append(name.Length).Append(':').Append(name).Append('=').Append(value.Length).Append(':').Append(value);
        }

        return builder.ToString();
    }
}
