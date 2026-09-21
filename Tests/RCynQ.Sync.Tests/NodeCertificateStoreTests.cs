using FluentAssertions;
using RCynQ.Core.Config;
using RCynQ.Sync.Endpoint;

namespace RCynQ.Sync.Tests;

/// <summary>
/// Tests of <see cref="NodeCertificateStore"/>.
/// </summary>
public sealed class NodeCertificateStoreTests : IDisposable
{
    /// <summary>The temporary data root.</summary>
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rcynq-tls", Guid.NewGuid().ToString("N"));

    /// <summary>The data directory layout.</summary>
    private readonly AppDataPaths _paths;

    /// <summary>
    /// Initializes a new instance of the <see cref="NodeCertificateStoreTests"/> class.
    /// </summary>
    public NodeCertificateStoreTests() => _paths = AppDataPaths.FromRoot(_root);

    /// <summary>
    /// Verifies that the certificate is created once, kept on disk and reused with the same fingerprint.
    /// </summary>
    [Fact]
    public void Certificate_Is_Created_Once_And_Reused()
    {
        using var created = NodeCertificateStore.GetOrCreate(_paths, "dest-1");

        created.HasPrivateKey.Should().BeTrue("Kestrel needs the private key");
        created.Subject.Should().Contain("rcynq-dest-1");
        File.Exists(Path.Combine(_paths.TlsDirectory, NodeCertificateStore.CertificateFileName)).Should().BeTrue();
        File.Exists(Path.Combine(_paths.TlsDirectory, NodeCertificateStore.PrivateKeyFileName)).Should().BeTrue();
        created.NotAfter.Should().BeAfter(DateTime.Now.AddYears(5));

        using var loaded = NodeCertificateStore.GetOrCreate(_paths, "dest-1");
        NodeCertificateStore.Fingerprint(loaded).Should().Be(NodeCertificateStore.Fingerprint(created), "the node keeps its identity across restarts");
    }

    /// <summary>
    /// Verifies that a node identifier unusable in a name does not break the certificate.
    /// </summary>
    [Fact]
    public void Unsafe_Node_Id_Is_Sanitized()
    {
        using var certificate = NodeCertificateStore.GetOrCreate(_paths, "dest 1/../evil");

        certificate.Subject.Should().Be("CN=rcynq-dest-1----evil");
    }

    /// <summary>
    /// Verifies fingerprint formatting, comparison and rejection of malformed values.
    /// </summary>
    [Fact]
    public void Fingerprint_Is_Normalized_And_Compared()
    {
        using var certificate = NodeCertificateStore.GetOrCreate(_paths, "dest-1");
        var fingerprint = NodeCertificateStore.Fingerprint(certificate);

        fingerprint.Should().StartWith("SHA256:").And.HaveLength(7 + 64);
        NodeCertificateStore.Matches(certificate, fingerprint).Should().BeTrue();
        NodeCertificateStore.Matches(certificate, fingerprint[7..].ToLowerInvariant()).Should().BeTrue("the prefix is optional");
        NodeCertificateStore.Matches(certificate, string.Join(':', Enumerable.Range(0, 32).Select(i => fingerprint.Substring(7 + (i * 2), 2)))).Should().BeTrue("separators are ignored");
        NodeCertificateStore.Matches(certificate, null).Should().BeFalse();

        using var other = NodeCertificateStore.GetOrCreate(AppDataPaths.FromRoot(Path.Combine(_root, "other")), "dest-1");
        NodeCertificateStore.Matches(other, fingerprint).Should().BeFalse("a different node must not pass the pinned check");

        NodeCertificateStore.NormalizeFingerprint("  ").Should().BeNull();
        ((Action)(() => NodeCertificateStore.NormalizeFingerprint("SHA256:not-a-fingerprint"))).Should().Throw<FormatException>();
        ((Action)(() => NodeCertificateStore.NormalizeFingerprint(new string('z', 64)))).Should().Throw<FormatException>();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Best effort cleanup.
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort cleanup.
        }
    }
}
