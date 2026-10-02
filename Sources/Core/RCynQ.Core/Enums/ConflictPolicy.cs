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
    /// The incoming files are merged into the local ones with Git, file by file (<c>git merge-file</c>), with the sync
    /// base as the common ancestor. Lines both sides changed are left between conflict markers for the user to edit.
    /// </summary>
    [Description("Merge the incoming files into the local ones with Git; colliding lines are left between conflict markers.")]
    UseGit = 3
}
