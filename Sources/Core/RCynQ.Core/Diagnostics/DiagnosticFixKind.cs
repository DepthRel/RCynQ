using System.ComponentModel;
using System.Text.Json.Serialization;
using RCynQ.Core.Validation;

namespace RCynQ.Core.Diagnostics;

/// <summary>
/// Defines the kinds of automatic fixes a diagnostic may propose.
/// </summary>
/// <remarks>
/// The auto-fixer executes only an explicit allow-list of safe kinds and only after user confirmation.
/// <see cref="GlobalSshConfigChange"/>, <see cref="DeleteUserData"/> and <see cref="SystemServiceChange"/>
/// exist so that such proposals can be represented and deterministically refused.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<DiagnosticFixKind>))]
public enum DiagnosticFixKind
{
    /// <summary>
    /// Create a missing directory.
    /// </summary>
    [Description("Create a missing directory.")]
    CreateDirectory = 1,

    /// <summary>
    /// Start a transport link.
    /// </summary>
    [Description("Start a transport link.")]
    StartTransport = 2,

    /// <summary>
    /// Add a restricted line to authorized_keys.
    /// </summary>
    [Description("Add a restricted authorized_keys line.")]
    AddRestrictedAuthorizedKey = 3,

    /// <summary>
    /// Restart the local loopback endpoint.
    /// </summary>
    [Description("Restart the local endpoint.")]
    RestartLocalEndpoint = 4,

    /// <summary>
    /// Change the global SSH client or server configuration (never executed automatically).
    /// </summary>
    [Description("Change global SSH configuration (forbidden).")]
    GlobalSshConfigChange = 100,

    /// <summary>
    /// Delete user data (never executed automatically).
    /// </summary>
    [Description("Delete user data (forbidden).")]
    DeleteUserData = 101,

    /// <summary>
    /// Change operating system services (never executed automatically).
    /// </summary>
    [Description("Change system services (forbidden).")]
    SystemServiceChange = 102
}
