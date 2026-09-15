using System.ComponentModel;
using System.Text.Json.Serialization;

namespace RCynQ.Core.Enums;

/// <summary>
/// Defines the user-selected strategy for a patch whose files conflict with local changes.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ConflictPolicy>))]
public enum ConflictPolicy
{
    /// <summary>
    /// The incoming patch is not applied, the local state stays unchanged and the patch
    /// is marked as rejected by the user.
    /// </summary>
    [Description("Do not apply the incoming patch; keep the local state.")]
    KeepLocal = 1,

    /// <summary>
    /// Conflicting local files are backed up and the incoming files are written over the local state.
    /// </summary>
    [Description("Back up conflicting local files and apply the incoming files over them.")]
    ApplyIncomingOverLocal = 2,

    /// <summary>
    /// Git tooling is used: <c>git apply --check</c>, then <c>git apply --3way</c> when possible,
    /// falling back to <c>git apply --reject</c>; remaining conflicts are left for manual resolution.
    /// </summary>
    [Description("Use git apply (--3way, then --reject) and leave remaining conflicts to the user.")]
    UseGit = 3
}
