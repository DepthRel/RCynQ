using System.Security.Cryptography;
using System.Text;
using RCynQ.Core.Abstractions;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Patches;

namespace RCynQ.Sync.Security;

/// <summary>
/// Settings of the replay guard.
/// </summary>
public sealed class ReplayGuardOptions
{
    /// <summary>
    /// Gets or sets the maximum accepted envelope age.
    /// </summary>
    public TimeSpan MaxAge { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// Gets or sets the tolerated clock skew for envelopes from the future.
    /// </summary>
    public TimeSpan MaxClockSkew { get; set; } = TimeSpan.FromMinutes(5);
}
