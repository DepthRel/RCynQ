using FluentAssertions;
using RCynQ.Core.Ids;
using RCynQ.Core.Patches;
using RCynQ.Git.Patches.Serialization;
using RCynQ.Git.Paths;
using RCynQ.Sync.Signing;

namespace RCynQ.Sync.Tests;

/// <summary>
/// Tests of the envelope of a delivery of commits: what is signed, how it travels and what is refused.
/// </summary>
public class CommitDeliveryEnvelopeTests
{
    /// <summary>A valid digest.</summary>
    private static readonly string _hash = new('a', 64);

    /// <summary>The commit the delivery is built on.</summary>
    private static readonly string _base = new('0', 40);

    /// <summary>The time the envelopes are created at.</summary>
    private static readonly DateTimeOffset _created = new(2026, 10, 23, 10, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Verifies that every fact of the commits is signed, so the Origin acts only on what the Workstation signed.
    /// </summary>
    [Fact]
    public void Every_Fact_Of_The_Commits_Is_Signed()
    {
        var id = Guid.NewGuid();
        var baseline = PayloadCanonicalizer.Build(Envelope(id, Delivery()));

        PayloadCanonicalizer.Build(Envelope(id, Delivery())).Should().Equal(baseline);
        PayloadCanonicalizer.Build(Envelope(id, null)).Should().NotEqual(baseline, "a patch of files signs differently");
        PayloadCanonicalizer.Build(Envelope(id, Delivery(branch: "release"))).Should().NotEqual(baseline);
        PayloadCanonicalizer.Build(Envelope(id, Delivery(baseCommit: new string('9', 40)))).Should().NotEqual(baseline);
        PayloadCanonicalizer.Build(Envelope(id, Delivery(subject: "other"))).Should().NotEqual(baseline);
        PayloadCanonicalizer.Build(Envelope(id, Delivery(size: 2))).Should().NotEqual(baseline);
        PayloadCanonicalizer.Build(Envelope(id, Delivery(bundle: new string('b', 64)))).Should().NotEqual(baseline);
    }

    /// <summary>
    /// Verifies that the commits survive the transfer format and that the bundle is the one piece of content.
    /// </summary>
    [Fact]
    public void Commits_Travel_And_Name_Only_Their_Bundle()
    {
        var serializer = new PatchEnvelopeSerializer(TestTexts.English, new PathUtility(TestTexts.English));
        var envelope = Envelope(Guid.NewGuid(), Delivery()).WithSignature(new byte[] { 1, 2 });

        var restored = serializer.Deserialize(serializer.Serialize(envelope));

        restored.Commits.Should().BeEquivalentTo(envelope.Commits);
        restored.ContentHashes.Should().Equal(_hash);
        PayloadCanonicalizer.Build(restored).Should().Equal(PayloadCanonicalizer.Build(envelope));
    }

    /// <summary>
    /// Verifies that malformed commits are refused when the envelope is read: a name that could reach Git as an option,
    /// a control character in a text, a list that carries the base, and files next to commits.
    /// </summary>
    [Fact]
    public void Malformed_Commits_Are_Refused()
    {
        var serializer = new PatchEnvelopeSerializer(TestTexts.English, new PathUtility(TestTexts.English));
        var dto = serializer.ToDto(Envelope(Guid.NewGuid(), Delivery()));

        dto.Commits!.Branch = "--upload-pack=evil";
        Read(serializer, dto).Should().Throw<FormatException>();
        dto = serializer.ToDto(Envelope(Guid.NewGuid(), Delivery()));
        dto.Commits!.Commits[0].Subject = "line\nbreak";
        Read(serializer, dto).Should().Throw<FormatException>();
        dto = serializer.ToDto(Envelope(Guid.NewGuid(), Delivery()));
        dto.Commits!.Commits[0].Hash = _base;
        Read(serializer, dto).Should().Throw<FormatException>();
        dto = serializer.ToDto(Envelope(Guid.NewGuid(), Delivery()));
        dto.Changes.Add(new FileChangeDto { Kind = Core.Enums.ChangeKind.Added, Path = "a.txt", Sha256 = _hash, SizeBytes = 1 });
        Read(serializer, dto).Should().Throw<FormatException>();
    }

    /// <summary>
    /// Creates a delivery of two commits.
    /// </summary>
    /// <param name="branch">The branch.</param>
    /// <param name="baseCommit">The commit it is built on.</param>
    /// <param name="subject">The subject of the first commit.</param>
    /// <param name="size">The size of the bundle.</param>
    /// <param name="bundle">The hash of the bundle.</param>
    /// <returns>The delivery.</returns>
    private static CommitDelivery Delivery(string branch = "main", string? baseCommit = null, string subject = "first", long size = 1, string? bundle = null)
        => new(
            branch,
            baseCommit ?? _base,
            new[]
            {
                new CommitInfo(new string('1', 40), subject, "dev <dev@example.com>", _created),
                new CommitInfo(new string('2', 40), "second", "dev <dev@example.com>", _created)
            },
            bundle ?? _hash,
            size);

    /// <summary>
    /// Creates an envelope.
    /// </summary>
    /// <param name="id">The identifier.</param>
    /// <param name="commits">The commits, or <see langword="null"/> for an empty patch of files.</param>
    /// <returns>The envelope.</returns>
    private static PatchEnvelope Envelope(Guid id, CommitDelivery? commits)
        => new(id, new NodeId("workstation"), new NodeId("origin"), _created, _base, _base, Array.Empty<FileChange>(), repository: "app", commits: commits);

    /// <summary>
    /// Reads a transfer object back.
    /// </summary>
    /// <param name="serializer">The serializer.</param>
    /// <param name="dto">The transfer object.</param>
    /// <returns>The read, for the assertion.</returns>
    private static Action Read(PatchEnvelopeSerializer serializer, PatchEnvelopeDto dto) => () => serializer.FromDto(dto);
}
