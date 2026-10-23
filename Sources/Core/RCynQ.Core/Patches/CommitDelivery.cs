using RCynQ.Core.Api;
using RCynQ.Core.Validation;

namespace RCynQ.Core.Patches;

/// <summary>
/// The commits a delivery carries: a block of commits of one branch built on the commit the receiver has on top of that
/// branch, and the Git bundle that holds them.
/// </summary>
/// <remarks>
/// Every field is part of what the sender signs. The receiver fast-forwards its checked out branch to <see cref="TipCommit"/>
/// only when that branch is <see cref="Branch"/>, its <c>HEAD</c> is <see cref="BaseCommit"/> and the bundle holds exactly
/// <see cref="Commits"/> on the first-parent chain from the base to the tip.
/// </remarks>
public sealed record CommitDelivery
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CommitDelivery"/> record.
    /// </summary>
    /// <param name="branch">The branch the commits belong to.</param>
    /// <param name="baseCommit">The parent of the first commit: the commit the receiver must have on top of the branch.</param>
    /// <param name="commits">The commits on the first-parent chain from the base to the tip, oldest first.</param>
    /// <param name="bundleSha256">The SHA-256 hash of the bundle.</param>
    /// <param name="bundleSizeBytes">The size of the bundle in bytes.</param>
    /// <exception cref="ArgumentException">A name, the list or the bundle facts are not valid.</exception>
    public CommitDelivery(string branch, string baseCommit, IReadOnlyList<CommitInfo> commits, string bundleSha256, long bundleSizeBytes)
    {
        if (!GitNames.IsBranch(branch))
        {
            throw new ArgumentException($"'{branch}' is not a safe branch name.", nameof(branch));
        }

        if (!GitNames.IsCommit(baseCommit))
        {
            throw new ArgumentException($"'{baseCommit}' is not a full commit name.", nameof(baseCommit));
        }

        ArgumentNullException.ThrowIfNull(commits);
        if (commits.Count is 0 or > ApiLimits.MAX_DELIVERY_COMMITS || commits.Any(commit => commit is null))
        {
            throw new ArgumentException($"A delivery carries from 1 to {ApiLimits.MAX_DELIVERY_COMMITS} commits.", nameof(commits));
        }

        var hashes = commits.Select(commit => commit.Hash).ToHashSet(StringComparer.Ordinal);
        if (hashes.Count != commits.Count || hashes.Contains(baseCommit))
        {
            throw new ArgumentException("The commits of a delivery are distinct and do not include the base.", nameof(commits));
        }

        Branch = branch;
        BaseCommit = baseCommit;
        Commits = commits.ToArray();
        BundleSha256 = Guard.Sha256Hex(bundleSha256, nameof(bundleSha256));
        BundleSizeBytes = Guard.NonNegative(bundleSizeBytes, nameof(bundleSizeBytes));
    }

    /// <summary>
    /// Gets the branch the commits belong to.
    /// </summary>
    public string Branch { get; }

    /// <summary>
    /// Gets the parent of the first commit.
    /// </summary>
    public string BaseCommit { get; }

    /// <summary>
    /// Gets the commits on the first-parent chain from the base to the tip, oldest first.
    /// </summary>
    public IReadOnlyList<CommitInfo> Commits { get; }

    /// <summary>
    /// Gets the last commit of the delivery: the commit the branch of the receiver moves to.
    /// </summary>
    public string TipCommit => Commits[^1].Hash;

    /// <summary>
    /// Gets the SHA-256 hash of the bundle.
    /// </summary>
    public string BundleSha256 { get; }

    /// <summary>
    /// Gets the size of the bundle in bytes.
    /// </summary>
    public long BundleSizeBytes { get; }
}
