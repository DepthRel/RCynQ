using System.Text.Json;
using RCynQ.Core.Abstractions.Paths;
using RCynQ.Core.Enums;
using RCynQ.Core.Ids;
using RCynQ.Core.Patches;
using RCynQ.Localization;

namespace RCynQ.Git.Patches.Serialization;

/// <summary>
/// Converts patch envelopes to and from their JSON transfer representation.
/// </summary>
/// <remarks>
/// Deserialization treats the input as untrusted: identifiers, hashes, the Base64 signature and every manifest path are
/// validated, and non-canonical or unsafe paths are rejected before anything touches the file system.
/// </remarks>
public sealed class PatchEnvelopeSerializer
{
    /// <summary>
    /// JSON options for the transfer format.
    /// </summary>
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// The path utility.
    /// </summary>
    private readonly IPathUtility _paths;

    /// <summary>
    /// Initializes a new instance of the <see cref="PatchEnvelopeSerializer"/> class.
    /// </summary>
    /// <param name="paths">The path utility.</param>
    public PatchEnvelopeSerializer(IPathUtility paths)
    {
        _paths = paths;
    }

    /// <summary>
    /// Converts an envelope to its transfer object.
    /// </summary>
    /// <param name="envelope">The envelope.</param>
    /// <returns>The transfer object.</returns>
    public PatchEnvelopeDto ToDto(PatchEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        return new PatchEnvelopeDto
        {
            PatchId = envelope.Id,
            Sender = envelope.SourceNode.Value,
            Receiver = envelope.TargetNode.Value,
            CreatedAt = envelope.CreatedAtUtc,
            BaseStateHash = envelope.BaseStateHash,
            IncomingStateHash = envelope.IncomingStateHash,
            Changes = envelope.Manifest.Select(change => new FileChangeDto
            {
                Kind = change.Kind,
                Path = change.Path,
                Sha256 = change.Sha256,
                SizeBytes = change.SizeBytes,
                IsSymlink = change.IsSymlink
            }).ToList(),
            Signature = envelope.IsSigned ? Convert.ToBase64String(envelope.Signature) : null
        };
    }

    /// <summary>
    /// Converts a transfer object to an envelope.
    /// </summary>
    /// <param name="dto">The transfer object.</param>
    /// <returns>The envelope.</returns>
    /// <exception cref="FormatException">The transfer object is malformed or unsafe.</exception>
    public PatchEnvelope FromDto(PatchEnvelopeDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);
        try
        {
            var manifest = (dto.Changes ?? new List<FileChangeDto>()).Select(change =>
            {
                var normalized = _paths.NormalizeRelativePath(change.Path);
                if (normalized != change.Path)
                {
                    throw new FormatException(Texts.Format("Envelope.PathNotCanonical", change.Path));
                }

                if (change.Kind == ChangeKind.Renamed || !Enum.IsDefined(change.Kind))
                {
                    throw new FormatException(Texts.Format("Envelope.KindUnsupported", change.Kind));
                }

                if (change.Kind != ChangeKind.Deleted && change.Sha256 is null)
                {
                    throw new FormatException(Texts.Format("Envelope.NeedsHash", change.Path));
                }

                // The size bounds the content the receiver accepts for the entry, so a compressed upload cannot expand without limit.
                if (change.Kind != ChangeKind.Deleted && change.SizeBytes is null)
                {
                    throw new FormatException(Texts.Format("Envelope.NeedsSize", change.Path));
                }

                return new FileChange(change.Kind, normalized, change.Sha256, change.SizeBytes, change.IsSymlink);
            }).ToArray();

            if (manifest.Select(change => change.Path).Distinct(StringComparer.Ordinal).Count() != manifest.Length)
            {
                throw new FormatException(Texts.Get("Envelope.DuplicatePaths"));
            }

            return new PatchEnvelope(
                dto.PatchId,
                NodeId.Parse(dto.Sender ?? string.Empty),
                NodeId.Parse(dto.Receiver ?? string.Empty),
                dto.CreatedAt,
                dto.BaseStateHash,
                dto.IncomingStateHash,
                manifest,
                string.IsNullOrEmpty(dto.Signature) ? null : Convert.FromBase64String(dto.Signature));
        }
        catch (ArgumentException ex)
        {
            throw new FormatException(Texts.Format("Envelope.Invalid", ex.Message), ex);
        }
    }

    /// <summary>
    /// Serializes an envelope to UTF-8 JSON.
    /// </summary>
    /// <param name="envelope">The envelope.</param>
    /// <returns>The JSON bytes.</returns>
    public byte[] Serialize(PatchEnvelope envelope) => JsonSerializer.SerializeToUtf8Bytes(ToDto(envelope), JsonOptions);

    /// <summary>
    /// Deserializes an envelope from UTF-8 JSON.
    /// </summary>
    /// <param name="json">The JSON bytes.</param>
    /// <returns>The envelope.</returns>
    /// <exception cref="FormatException">The JSON is malformed or the envelope is invalid.</exception>
    public PatchEnvelope Deserialize(ReadOnlySpan<byte> json)
    {
        PatchEnvelopeDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<PatchEnvelopeDto>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new FormatException(Texts.Format("Envelope.Malformed", ex.Message), ex);
        }

        return FromDto(dto ?? throw new FormatException(Texts.Get("Envelope.Empty")));
    }
}
