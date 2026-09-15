namespace RCynQ.Core.Data.Entities;

/// <summary>
/// EF Core entity for an encrypted secret (table <c>Secrets</c>).
/// </summary>
public sealed class SecretEntity
{
    /// <summary>Gets or sets the logical secret name (primary key).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the AES-GCM nonce.</summary>
    public byte[] Nonce { get; set; } = Array.Empty<byte>();

    /// <summary>Gets or sets the AES-GCM ciphertext.</summary>
    public byte[] CipherText { get; set; } = Array.Empty<byte>();

    /// <summary>Gets or sets the AES-GCM authentication tag.</summary>
    public byte[] Tag { get; set; } = Array.Empty<byte>();

    /// <summary>Gets or sets the time the secret was last updated.</summary>
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
