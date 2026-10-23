using System.Text.Json.Serialization;
using RCynQ.Core.Enums;

namespace RCynQ.Core.State;

/// <summary>
/// Describes the sync base of a node: where it comes from and, when there is none, why.
/// </summary>
/// <param name="Mode">Where the sync base comes from; <see cref="SyncBaseMode.None"/> when there is none.</param>
/// <param name="Branch">The branch the Git sync base follows, or <see langword="null"/> outside <see cref="SyncBaseMode.Git"/> or for a detached HEAD.</param>
/// <param name="Commit">The commit the Git sync base was built from, or <see langword="null"/> outside <see cref="SyncBaseMode.Git"/>.</param>
/// <param name="Problem">Why there is no sync base, in the language of the request; <see langword="null"/> when the base is ready.</param>
/// <param name="CanCreateManual">
/// <see langword="true"/> when the node may take (or retake) a manual snapshot: its working tree cannot provide a Git sync base.
/// </param>
public sealed record SyncBaseStatus(SyncBaseMode Mode, string? Branch, string? Commit, string? Problem, bool CanCreateManual)
{
    /// <summary>
    /// Gets a value indicating whether the node has a sync base and can send and receive patches.
    /// </summary>
    [JsonIgnore]
    public bool IsReady => Mode != SyncBaseMode.None;

    /// <summary>
    /// Gets the commit the changes of the working tree are counted from when it is not <see cref="Commit"/>: <c>HEAD</c> of a
    /// Workstation whose own commits on top of the commit of the Origin go as commits. <see langword="null"/> counts from
    /// <see cref="Commit"/>.
    /// </summary>
    [JsonIgnore]
    public string? ChangesSince { get; init; }

    /// <summary>
    /// Creates the status of a node without a sync base.
    /// </summary>
    /// <param name="problem">Why there is no sync base.</param>
    /// <param name="canCreateManual">Whether a manual snapshot may be taken.</param>
    /// <returns>The status.</returns>
    public static SyncBaseStatus NotReady(string problem, bool canCreateManual) => new(SyncBaseMode.None, null, null, problem, canCreateManual);

    /// <summary>
    /// Gets the status of a node that does not take part in the patch channel.
    /// </summary>
    public static SyncBaseStatus NotApplicable { get; } = new(SyncBaseMode.None, null, null, null, false);
}
