using System.Globalization;
using System.Text;
using RCynQ.Core.Patches;

namespace RCynQ.Sync.Signing;

/// <summary>
/// Builds the deterministic byte payload covered by a patch signature.
/// </summary>
/// <remarks>
/// Every field is length-prefixed (<c>length:value\n</c>), so no path content can make two different envelopes
/// produce the same payload. File content is represented by the SHA-256 hashes in the manifest, so the signature covers every file that follows the manifest.
/// </remarks>
public static class PayloadCanonicalizer
{
    /// <summary>
    /// The payload format version.
    /// </summary>
    /// <remarks>v3 adds the hash of each file in the sender's sync base, which decides conflicts on the receiver.</remarks>
    public const string VERSION = "rcynq-patch-v3";

    /// <summary>
    /// Builds the canonical payload of an envelope (the signature itself is excluded).
    /// </summary>
    /// <param name="envelope">The envelope.</param>
    /// <returns>The payload bytes.</returns>
    public static byte[] Build(PatchEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        var builder = new StringBuilder();
        Append(builder, VERSION);
        Append(builder, envelope.Id.ToString("D"));
        Append(builder, envelope.SourceNode.Value);
        Append(builder, envelope.TargetNode.Value);
        Append(builder, envelope.CreatedAtUtc.UtcTicks.ToString(CultureInfo.InvariantCulture));
        Append(builder, envelope.BaseStateHash);
        Append(builder, envelope.IncomingStateHash);
        Append(builder, envelope.Manifest.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var change in envelope.Manifest)
        {
            Append(builder, ((int)change.Kind).ToString(CultureInfo.InvariantCulture));
            Append(builder, change.Path);
            Append(builder, change.Sha256 ?? "-");
            Append(builder, change.SizeBytes?.ToString(CultureInfo.InvariantCulture) ?? "-");
            Append(builder, change.IsSymlink ? "1" : "0");
            Append(builder, change.BaseSha256 ?? "-");
        }

        // Named only when present, so an envelope without a repository signs as it always did.
        if (envelope.Repository is not null)
        {
            Append(builder, "repository");
            Append(builder, envelope.Repository);
        }

        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    /// <summary>
    /// Appends a length-prefixed field.
    /// </summary>
    /// <param name="builder">The builder.</param>
    /// <param name="value">The field value.</param>
    private static void Append(StringBuilder builder, string value)
        => builder.Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value).Append('\n');
}
