using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using RCynQ.Localization;

namespace RCynQ.Ssh.Keys;

/// <summary>
/// A parsed OpenSSH public key (<c>type base64-blob [comment]</c>).
/// </summary>
public sealed record SshPublicKey
{
    /// <summary>
    /// Key types accepted for RCynQ keys.
    /// </summary>
    public static readonly IReadOnlySet<string> AllowedTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "ssh-ed25519",
        "ecdsa-sha2-nistp256",
        "ecdsa-sha2-nistp384",
        "ecdsa-sha2-nistp521",
        "ssh-rsa",
        "sk-ssh-ed25519@openssh.com",
        "sk-ecdsa-sha2-nistp256@openssh.com"
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="SshPublicKey"/> record.
    /// </summary>
    /// <param name="type">The key type.</param>
    /// <param name="blob">The Base64 key blob.</param>
    /// <param name="comment">The comment, or <see langword="null"/>.</param>
    /// <exception cref="FormatException">The key is malformed.</exception>
    public SshPublicKey(string type, string blob, string? comment)
    {
        if (!AllowedTypes.Contains(type))
        {
            throw new FormatException(Texts.Format("Keys.TypeUnsupported", type));
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(blob);
        }
        catch (FormatException ex)
        {
            throw new FormatException(Texts.Get("Keys.BlobBase64"), ex);
        }

        if (bytes.Length < 4)
        {
            throw new FormatException(Texts.Get("Keys.BlobShort"));
        }

        var typeLength = BinaryPrimitives.ReadUInt32BigEndian(bytes);
        if (typeLength > bytes.Length - 4 || Encoding.ASCII.GetString(bytes, 4, (int)typeLength) != type)
        {
            throw new FormatException(Texts.Get("Keys.BlobType"));
        }

        Type = type;
        Blob = blob;
        BlobBytes = bytes;
        Comment = string.IsNullOrWhiteSpace(comment) ? null : new string(comment.Where(c => !char.IsControl(c)).ToArray()).Trim();
    }

    /// <summary>Gets the key type.</summary>
    public string Type { get; }

    /// <summary>Gets the Base64 key blob.</summary>
    public string Blob { get; }

    /// <summary>Gets the decoded key blob.</summary>
    public byte[] BlobBytes { get; }

    /// <summary>Gets the comment.</summary>
    public string? Comment { get; }

    /// <summary>
    /// Gets the OpenSSH SHA-256 fingerprint (<c>SHA256:</c> followed by unpadded Base64).
    /// </summary>
    public string Fingerprint => ComputeFingerprint(BlobBytes);

    /// <summary>
    /// Formats the key as a single line.
    /// </summary>
    /// <returns><c>type blob [comment]</c>.</returns>
    public string ToLine() => Comment is null ? $"{Type} {Blob}" : $"{Type} {Blob} {Comment}";

    /// <summary>
    /// Parses a public key line.
    /// </summary>
    /// <param name="line">The line.</param>
    /// <returns>The key.</returns>
    /// <exception cref="FormatException">The line is not a supported public key.</exception>
    public static SshPublicKey Parse(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var parts = line.Trim().Split((char[]?)null, 3, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            throw new FormatException(Texts.Get("Keys.Form"));
        }

        return new SshPublicKey(parts[0], parts[1], parts.Length == 3 ? parts[2] : null);
    }

    /// <summary>
    /// Computes the OpenSSH SHA-256 fingerprint of a key blob.
    /// </summary>
    /// <param name="blob">The decoded blob.</param>
    /// <returns>The fingerprint.</returns>
    public static string ComputeFingerprint(byte[] blob)
        => "SHA256:" + Convert.ToBase64String(SHA256.HashData(blob)).TrimEnd('=');

    /// <summary>
    /// Normalizes a fingerprint for comparison (adds the <c>SHA256:</c> prefix and removes padding).
    /// </summary>
    /// <param name="fingerprint">The fingerprint.</param>
    /// <returns>The normalized fingerprint.</returns>
    public static string NormalizeFingerprint(string fingerprint)
    {
        var value = fingerprint.Trim();
        if (!value.StartsWith("SHA256:", StringComparison.OrdinalIgnoreCase))
        {
            value = "SHA256:" + value;
        }

        return "SHA256:" + value["SHA256:".Length..].TrimEnd('=');
    }
}
