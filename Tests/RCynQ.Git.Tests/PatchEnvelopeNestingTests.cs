using FluentAssertions;
using RCynQ.Core.Enums;
using RCynQ.Core.Ids;
using RCynQ.Core.Patches;
using RCynQ.Git.Patches.Serialization;
using RCynQ.Git.Paths;

namespace RCynQ.Git.Tests;

/// <summary>
/// Tests of <see cref="PatchEnvelopeSerializer"/> for manifests whose entries lie below one another.
/// </summary>
/// <remarks>
/// An entry is a file or a link, never a directory. A path below an entry that stays can only be reached through a link
/// the same patch creates, which is how a patch would write outside the tree, so such a manifest is refused as soon as it
/// is read.
/// </remarks>
public sealed class PatchEnvelopeNestingTests
{
    /// <summary>A valid SHA-256 digest.</summary>
    private static readonly string _hash = new('a', 64);

    /// <summary>The serializer under test.</summary>
    private readonly PatchEnvelopeSerializer _serializer = new(TestTexts.English, new PathUtility(TestTexts.English));

    /// <summary>
    /// Verifies that an entry below a link or a file of the same manifest is refused, naming both entries.
    /// </summary>
    /// <param name="kind">The kind of the entry above.</param>
    /// <param name="isLink">Whether the entry above is a link.</param>
    [Theory]
    [InlineData(ChangeKind.Added, true)]
    [InlineData(ChangeKind.Modified, true)]
    [InlineData(ChangeKind.Added, false)]
    public void An_Entry_Below_An_Entry_That_Stays_Is_Refused(ChangeKind kind, bool isLink)
    {
        var json = Serialize(
            new FileChange(kind, "w", _hash, 2, isLink, kind == ChangeKind.Modified ? _hash : null),
            new FileChange(ChangeKind.Added, "w/deep/x.txt", _hash, 2, false));

        var read = () => _serializer.Deserialize(json);

        read.Should().Throw<FormatException>()
            .Which.Message.Should().Contain(TestTexts.English.Format("Envelope.PathBelowEntry", "w/deep/x.txt", "w"));
    }

    /// <summary>
    /// Verifies that a file may take the place of a deleted entry's directory, and that entries that only share the
    /// beginning of a name are not nested.
    /// </summary>
    [Fact]
    public void An_Entry_Below_A_Deleted_Entry_Or_Next_To_A_Similar_Name_Is_Accepted()
    {
        var json = Serialize(
            new FileChange(ChangeKind.Deleted, "a", null, null, true, _hash),
            new FileChange(ChangeKind.Added, "a/x.txt", _hash, 2, false),
            new FileChange(ChangeKind.Added, "ab", _hash, 2, true),
            new FileChange(ChangeKind.Added, "ab.txt", _hash, 2, false),
            new FileChange(ChangeKind.Added, "abc/x.txt", _hash, 2, false));

        _serializer.Deserialize(json).Manifest.Should().HaveCount(5);
    }

    /// <summary>
    /// Serializes a manifest of the given entries.
    /// </summary>
    /// <param name="changes">The entries.</param>
    /// <returns>The JSON of the envelope.</returns>
    private byte[] Serialize(params FileChange[] changes)
        => _serializer.Serialize(new PatchEnvelope(Guid.NewGuid(), new NodeId("workstation-1"), new NodeId("origin-1"), DateTimeOffset.UtcNow, _hash, _hash, changes));
}
