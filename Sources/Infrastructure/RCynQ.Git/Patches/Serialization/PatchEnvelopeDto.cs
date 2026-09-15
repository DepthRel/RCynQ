using System.Text.Json;
using RCynQ.Core.Abstractions;
using RCynQ.Core.Enums;
using RCynQ.Core.Ids;
using RCynQ.Core.Patches;

namespace RCynQ.Git.Patches.Serialization;

/// <summary>
/// HTTP transfer object of a patch envelope.
/// </summary>
public sealed class PatchEnvelopeDto
{
    /// <summary>Gets or sets the patch identifier.</summary>
    public Guid PatchId { get; set; }

    /// <summary>Gets or sets the sender node identifier.</summary>
    public string Sender { get; set; } = string.Empty;

    /// <summary>Gets or sets the receiver node identifier.</summary>
    public string Receiver { get; set; } = string.Empty;

    /// <summary>Gets or sets the creation time.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Gets or sets the sender's base state hash.</summary>
    public string BaseStateHash { get; set; } = string.Empty;

    /// <summary>Gets or sets the sender's working tree hash.</summary>
    public string IncomingStateHash { get; set; } = string.Empty;

    /// <summary>Gets or sets the file changes.</summary>
    public List<FileChangeDto> Changes { get; set; } = new();

    /// <summary>Gets or sets the Base64 unified diff.</summary>
    public string UnifiedDiffBase64 { get; set; } = string.Empty;

    /// <summary>Gets or sets the Base64 file bundle.</summary>
    public string IncomingFileBundleBase64 { get; set; } = string.Empty;

    /// <summary>Gets or sets the Base64 signature.</summary>
    public string? Signature { get; set; }
}
