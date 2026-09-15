using System.Net.Http.Headers;
using System.Text.Json;
using RCynQ.Core.Abstractions;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Patches;
using RCynQ.Git.Patches;
using RCynQ.Ssh.Commands;
using RCynQ.Sync.Security;
using RCynQ.Sync.Signing;

namespace RCynQ.Sync.Transfer;

/// <summary>
/// Settings of the HTTP patch sender.
/// </summary>
public sealed class HttpPatchSenderOptions
{
    /// <summary>
    /// Gets or sets the timeout of one HTTP attempt.
    /// </summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Gets or sets the delay before the single network retry.
    /// </summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(2);
}
