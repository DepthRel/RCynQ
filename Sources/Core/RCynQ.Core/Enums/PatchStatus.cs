using System.ComponentModel;
using System.Text.Json.Serialization;

namespace RCynQ.Core.Enums;

/// <summary>
/// Defines the lifecycle status of a patch.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<PatchStatus>))]
public enum PatchStatus
{
    /// <summary>
    /// The patch has been created or received and stored, but not processed yet.
    /// </summary>
    [Description("Stored but not processed yet.")]
    Pending = 1,

    /// <summary>
    /// The patch has been applied cleanly.
    /// </summary>
    [Description("Applied cleanly.")]
    Applied = 2,

    /// <summary>
    /// The patch was rejected, either by the user (<see cref="ConflictPolicy.KeepLocal"/>) or by validation.
    /// </summary>
    [Description("Rejected by the user or by validation.")]
    Rejected = 3,

    /// <summary>
    /// The patch conflicts with local changes and waits for a user decision.
    /// </summary>
    [Description("Conflicts with local changes; waiting for a user decision.")]
    ConflictPending = 4,

    /// <summary>
    /// The conflicts of the patch were resolved by the user.
    /// </summary>
    [Description("Conflicts were resolved.")]
    ConflictResolved = 5,

    /// <summary>
    /// Processing the patch failed.
    /// </summary>
    [Description("Processing failed.")]
    Failed = 6
}
