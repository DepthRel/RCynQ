using FluentAssertions;
using NSubstitute;
using RCynQ.Core.Abstractions;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Ids;
using RCynQ.Core.Patches;
using RCynQ.Sync.Security;
using RCynQ.Sync.Signing;
using RCynQ.Sync.Tests.Fakes;
using Xunit;

namespace RCynQ.Sync.Tests;

/// <summary>
/// Tests for signing, the canonical payload, replay protection and the link token.
/// </summary>
public class SignatureAndReplayTests
{
    /// <summary>
    /// A valid digest.
    /// </summary>
    private static readonly string Hash = new('a', 64);

    /// <summary>
    /// Verifies sign/verify round-trip, key persistence and rejection of tampered data or foreign keys.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Ecdsa_Signs_And_Verifies()
    {
        var secrets = new InMemorySecretStore();
        using var signer = new EcdsaSignatureService(secrets);
        var payload = "payload"u8.ToArray();

        var signature = await signer.SignAsync(payload);
        var publicKey = await signer.ExportPublicKeyAsync();

        signer.Verify(payload, signature, publicKey).Should().BeTrue();
        signer.Verify("tampered"u8.ToArray(), signature, publicKey).Should().BeFalse();
        signer.Verify(payload, signature, await new EcdsaSignatureService(new InMemorySecretStore()).ExportPublicKeyAsync()).Should().BeFalse();
        signer.Verify(payload, signature, new byte[] { 1, 2, 3 }).Should().BeFalse();

        (await secrets.GetAsync(EcdsaSignatureService.SecretName)).Should().NotBeNullOrEmpty();
        using var reloaded = new EcdsaSignatureService(secrets);
        (await reloaded.ExportPublicKeyAsync()).Should().Equal(publicKey);
    }

    /// <summary>
    /// Verifies that the canonical payload is stable and covers every signed field.
    /// </summary>
    [Fact]
    public void Canonical_Payload_Covers_All_Fields()
    {
        var created = new DateTimeOffset(2026, 9, 14, 10, 0, 0, TimeSpan.Zero);
        var id = Guid.NewGuid();
        PatchEnvelope Envelope(string path = "a.txt", DateTimeOffset? at = null, byte[]? diff = null, string target = "dest")
            => new(id, new NodeId("source"), new NodeId(target), at ?? created, Hash, Hash,
                new[] { new FileChange(ChangeKind.Modified, path, Hash, 1, false) }, diff ?? new byte[] { 1 }, new byte[] { 2 });

        var baseline = PayloadCanonicalizer.Build(Envelope());
        PayloadCanonicalizer.Build(Envelope()).Should().Equal(baseline);
        PayloadCanonicalizer.Build(Envelope().WithSignature(new byte[] { 9 })).Should().Equal(baseline, "the signature is not part of the payload");
        PayloadCanonicalizer.Build(Envelope(path: "b.txt")).Should().NotEqual(baseline);
        PayloadCanonicalizer.Build(Envelope(at: created.AddTicks(1))).Should().NotEqual(baseline);
        PayloadCanonicalizer.Build(Envelope(diff: new byte[] { 3 })).Should().NotEqual(baseline);
        PayloadCanonicalizer.Build(Envelope(target: "other")).Should().NotEqual(baseline);
    }

    /// <summary>
    /// Verifies the replay guard decisions.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task ReplayGuard_Rejects_Replays_And_Stale_Envelopes()
    {
        var now = new DateTimeOffset(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);
        var clock = Substitute.For<TimeProvider>();
        clock.GetUtcNow().Returns(now);
        var repository = new InMemoryPatchRepository();
        var guard = new ReplayGuard(repository, clock, new ReplayGuardOptions { MaxAge = TimeSpan.FromHours(1), MaxClockSkew = TimeSpan.FromMinutes(5) });
        PatchEnvelope At(DateTimeOffset createdAt) => new(Guid.NewGuid(), new NodeId("s"), new NodeId("d"), createdAt, Hash, Hash, Array.Empty<FileChange>(), Array.Empty<byte>(), Array.Empty<byte>());

        var fresh = At(now.AddMinutes(-10));
        await guard.EnsureFreshAsync(fresh);
        await guard.EnsureFreshAsync(At(now.AddMinutes(4)));

        await repository.UpsertAsync(PatchMetadata.FromEnvelope(fresh, PatchDirection.Incoming));
        (await Reason(() => guard.EnsureFreshAsync(fresh))).Should().Be(PatchRejectionReason.Replay);
        (await Reason(() => guard.EnsureFreshAsync(At(now.AddHours(-2))))).Should().Be(PatchRejectionReason.Expired);
        (await Reason(() => guard.EnsureFreshAsync(At(now.AddMinutes(10))))).Should().Be(PatchRejectionReason.Expired);
    }

    /// <summary>
    /// Verifies token comparison and the secret store fallback.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task LinkToken_Matching_And_Fallback()
    {
        LinkTokenProvider.Matches("secret-token", "secret-token").Should().BeTrue();
        LinkTokenProvider.Matches("secret-token", "secret-tokeN").Should().BeFalse();
        LinkTokenProvider.Matches("secret-token", null).Should().BeFalse();
        LinkTokenProvider.Matches(null, "anything").Should().BeFalse();
        LinkTokenProvider.Matches("", "").Should().BeFalse();

        var secrets = new InMemorySecretStore();
        await secrets.SetAsync(LinkTokenProvider.SecretName, "from-secret-store");
        (await new LinkTokenProvider(new SyncBridgeOptions(), secrets).GetTokenAsync()).Should().Be("from-secret-store");
        (await new LinkTokenProvider(new SyncBridgeOptions { LinkToken = " configured " }, secrets).GetTokenAsync()).Should().Be("configured");
    }

    /// <summary>
    /// Runs an action and returns the rejection reason.
    /// </summary>
    /// <param name="action">The action.</param>
    /// <returns>The reason.</returns>
    private static async Task<PatchRejectionReason> Reason(Func<Task> action)
    {
        var assertion = await action.Should().ThrowAsync<PatchRejectedException>();
        return assertion.Which.Reason;
    }
}
