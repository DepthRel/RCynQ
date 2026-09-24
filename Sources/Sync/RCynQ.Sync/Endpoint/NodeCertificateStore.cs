using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using RCynQ.Core.Config;
using RCynQ.Core.Data.Storage;

namespace RCynQ.Sync.Endpoint;

/// <summary>
/// Creates and loads the self-signed TLS certificate that the direct endpoint of the node presents to neighbours.
/// </summary>
/// <remarks>
/// <para>
/// There is no certificate authority: a neighbour pins the SHA-256 fingerprint published in the node's export file,
/// exactly as it pins the sshd host key in tunnel mode. The certificate and its key live next to the other node keys
/// in <c>data/keys/tls</c> and are readable only by the owner.
/// </para>
/// <para>
/// The key is stored as PEM so that nothing is written outside the data directory. Windows SChannel cannot use a key
/// loaded from PEM directly, so on Windows the pair is re-imported through a PKCS#12 blob held in memory.
/// </para>
/// </remarks>
public static class NodeCertificateStore
{
    /// <summary>
    /// The certificate file name inside <see cref="AppDataPaths.TlsDirectory"/>.
    /// </summary>
    public const string CertificateFileName = "node.crt";

    /// <summary>
    /// The private key file name inside <see cref="AppDataPaths.TlsDirectory"/>.
    /// </summary>
    public const string PrivateKeyFileName = "node.key";

    /// <summary>
    /// The prefix of a formatted fingerprint.
    /// </summary>
    public const string FingerprintPrefix = "SHA256:";

    /// <summary>
    /// The certificate lifetime.
    /// </summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(3650);

    /// <summary>
    /// Loads the node certificate, creating it on first use.
    /// </summary>
    /// <param name="paths">The data directory layout.</param>
    /// <param name="nodeId">The node identifier, used as the certificate subject.</param>
    /// <param name="timeProvider">The time source, or <see langword="null"/> for the system clock.</param>
    /// <returns>The certificate with its private key. The caller owns it and should dispose it.</returns>
    /// <exception cref="ArgumentException"><paramref name="nodeId"/> is blank.</exception>
    public static X509Certificate2 GetOrCreate(AppDataPaths paths, string nodeId, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);

        var certificatePath = Path.Combine(paths.TlsDirectory, CertificateFileName);
        var keyPath = Path.Combine(paths.TlsDirectory, PrivateKeyFileName);
        if (!File.Exists(certificatePath) || !File.Exists(keyPath))
        {
            Create(paths.TlsDirectory, certificatePath, keyPath, nodeId, timeProvider ?? TimeProvider.System);
        }

        return Load(certificatePath, keyPath);
    }

    /// <summary>
    /// Gets the pinned fingerprint of a certificate.
    /// </summary>
    /// <param name="certificate">The certificate.</param>
    /// <returns>The fingerprint, for example <c>SHA256:A1B2…</c>.</returns>
    public static string Fingerprint(X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        return FingerprintPrefix + Convert.ToHexString(SHA256.HashData(certificate.RawData));
    }

    /// <summary>
    /// Normalizes a fingerprint written by a person or copied from another tool.
    /// </summary>
    /// <param name="value">The fingerprint, with or without the <c>SHA256:</c> prefix and separators.</param>
    /// <returns>The normalized fingerprint, or <see langword="null"/> when <paramref name="value"/> is blank.</returns>
    /// <exception cref="FormatException">The value is not a SHA-256 fingerprint.</exception>
    public static string? NormalizeFingerprint(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.StartsWith(FingerprintPrefix, StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[FingerprintPrefix.Length..];
        }

        var hex = trimmed.Replace(":", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToUpperInvariant();
        if (hex.Length != 64 || !hex.All(Uri.IsHexDigit))
        {
            throw new FormatException($"'{value}' is not a SHA-256 certificate fingerprint.");
        }

        return FingerprintPrefix + hex;
    }

    /// <summary>
    /// Checks a certificate against a pinned fingerprint.
    /// </summary>
    /// <param name="certificate">The certificate presented by the peer.</param>
    /// <param name="fingerprint">The pinned fingerprint.</param>
    /// <returns><see langword="true"/> when the certificate is the pinned one.</returns>
    /// <exception cref="FormatException"><paramref name="fingerprint"/> is not a SHA-256 fingerprint.</exception>
    public static bool Matches(X509Certificate2? certificate, string? fingerprint)
    {
        var expected = NormalizeFingerprint(fingerprint);
        return certificate is not null
            && expected is not null
            && string.Equals(Fingerprint(certificate), expected, StringComparison.Ordinal);
    }

    /// <summary>
    /// Writes a new self-signed certificate and its key.
    /// </summary>
    /// <param name="directory">The TLS directory.</param>
    /// <param name="certificatePath">The certificate file.</param>
    /// <param name="keyPath">The key file.</param>
    /// <param name="nodeId">The node identifier.</param>
    /// <param name="timeProvider">The time source.</param>
    private static void Create(string directory, string certificatePath, string keyPath, string nodeId, TimeProvider timeProvider)
    {
        Directory.CreateDirectory(directory);
        FilePermissions.RestrictDirectoryToOwner(directory);

        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN={CertificateSubject(nodeId)}", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, critical: true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, critical: false));

        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName(CertificateSubject(nodeId));
        names.AddDnsName("localhost");
        request.CertificateExtensions.Add(names.Build());

        var now = timeProvider.GetUtcNow();
        using var certificate = request.CreateSelfSigned(now.AddDays(-1), now.Add(Lifetime));
        File.WriteAllText(certificatePath, certificate.ExportCertificatePem());
        File.WriteAllText(keyPath, key.ExportPkcs8PrivateKeyPem());
        FilePermissions.RestrictFileToOwner(keyPath);
    }

    /// <summary>
    /// Builds the certificate subject of a node, keeping only characters valid in a DNS name.
    /// </summary>
    /// <param name="nodeId">The node identifier.</param>
    /// <returns>The subject.</returns>
    private static string CertificateSubject(string nodeId)
    {
        var safe = new string(nodeId.Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : '-').ToArray());
        return "rcynq-" + safe;
    }

    /// <summary>
    /// Loads the certificate with its private key.
    /// </summary>
    /// <param name="certificatePath">The certificate file.</param>
    /// <param name="keyPath">The key file.</param>
    /// <returns>The certificate.</returns>
    private static X509Certificate2 Load(string certificatePath, string keyPath)
    {
        var loaded = X509Certificate2.CreateFromPemFile(certificatePath, keyPath);
        if (!OperatingSystem.IsWindows())
        {
            return loaded;
        }

        using (loaded)
        {
            return X509CertificateLoader.LoadPkcs12(loaded.Export(X509ContentType.Pkcs12), null);
        }
    }
}
