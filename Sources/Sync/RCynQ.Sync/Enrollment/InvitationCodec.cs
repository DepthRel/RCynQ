using System.Buffers.Text;
using System.IO.Compression;
using System.Text.Json;
using RCynQ.Core.Api;
using RCynQ.Core.Enums;
using RCynQ.Localization;
using RCynQ.Sync.Configuration;
using RCynQ.Sync.Security;

namespace RCynQ.Sync.Enrollment;

/// <summary>
/// Writes an invitation as one line of text and reads it back: <see cref="Invitation.PREFIX"/> followed by the Base64url
/// of the deflated JSON. Everything read is checked before use: the text came from another computer.
/// </summary>
public static class InvitationCodec
{
    /// <summary>The longest invitation text accepted, in characters.</summary>
    public const int MAX_TEXT_LENGTH = 32 * 1024;

    /// <summary>The most Relays an invitation may name on the way.</summary>
    public const int MAX_PATH = 4;

    /// <summary>The longest JSON an invitation may inflate to, in bytes; a larger one is refused while it inflates.</summary>
    private const int MAX_JSON_BYTES = 256 * 1024;

    /// <summary>The longest secret of a ticket, in characters.</summary>
    private const int MAX_SECRET_LENGTH = 128;

    /// <summary>The longest one-time private key, in characters.</summary>
    private const int MAX_KEY_LENGTH = 4096;

    /// <summary>The first line of an OpenSSH private key.</summary>
    private const string PRIVATE_KEY_HEADER = "-----BEGIN OPENSSH PRIVATE KEY-----";

    /// <summary>The JSON options of the invitation.</summary>
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Writes an invitation as text.
    /// </summary>
    /// <param name="invitation">The invitation.</param>
    /// <returns>The text.</returns>
    public static string Encode(Invitation invitation)
    {
        ArgumentNullException.ThrowIfNull(invitation);

        using var buffer = new MemoryStream();
        using (var deflate = new DeflateStream(buffer, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            JsonSerializer.Serialize(deflate, invitation, _json);
        }

        return Invitation.PREFIX + Base64Url.EncodeToString(buffer.ToArray());
    }

    /// <summary>
    /// Reads an invitation from text and checks it.
    /// </summary>
    /// <param name="text">The text; spaces and line breaks around and inside it are ignored, as a mail program adds them.</param>
    /// <returns>The invitation.</returns>
    /// <exception cref="FormatException">The text is not a valid invitation; the message says why.</exception>
    public static Invitation Decode(string? text)
    {
        var compact = string.Concat((text ?? string.Empty).Where(c => !char.IsWhiteSpace(c)));
        if (!compact.StartsWith(Invitation.PREFIX, StringComparison.Ordinal))
        {
            throw LocalizedError.Format("Invitation.NotInvitation");
        }

        if (compact.Length > MAX_TEXT_LENGTH)
        {
            throw LocalizedError.Format("Invitation.TooLong");
        }

        Invitation? invitation;
        try
        {
            var packed = Base64Url.DecodeFromChars(compact.AsSpan(Invitation.PREFIX.Length));
            invitation = JsonSerializer.Deserialize<Invitation>(Inflate(packed), _json);
        }
        catch (Exception ex) when (ex is FormatException or JsonException or InvalidDataException)
        {
            throw LocalizedError.Create(message => new FormatException(message, ex), "Invitation.Damaged");
        }

        Validate(invitation);
        return invitation!;
    }

    /// <summary>
    /// Checks an invitation read from text.
    /// </summary>
    /// <param name="invitation">The invitation.</param>
    /// <exception cref="FormatException">The invitation is invalid.</exception>
    private static void Validate(Invitation? invitation)
    {
        if (invitation?.Inviter is null || invitation.Ticket is null || invitation.Path is null)
        {
            throw LocalizedError.Format("Invitation.Damaged");
        }

        if (invitation.Version != Invitation.CURRENT_VERSION)
        {
            throw LocalizedError.Format("Invitation.Version", invitation.Version);
        }

        if (invitation.Path.Count > MAX_PATH || invitation.Path.Any(card => card is null || card.Role != NodeRole.Relay))
        {
            throw LocalizedError.Format("Invitation.Damaged");
        }

        foreach (var card in invitation.Path.Prepend(invitation.Inviter))
        {
            NodeConfigurationTransfer.Validate(card);
        }

        ValidateTicket(invitation.Ticket);
        if (invitation.RelayTicket is not null)
        {
            ValidateTicket(invitation.RelayTicket);
        }

        if (invitation.LinkToken is { } token && (token.Length < LinkTokenProvider.MINIMUM_LENGTH || token.Any(c => char.IsControl(c) || char.IsWhiteSpace(c))))
        {
            throw LocalizedError.Format("Invitation.Damaged");
        }
    }

    /// <summary>
    /// Tells whether a ticket that came from another node may go into an invitation: an identifier, a secret and a
    /// one-time key of the expected form, and an endpoint role.
    /// </summary>
    /// <param name="ticket">The ticket, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the ticket is well-formed.</returns>
    public static bool IsValidTicket(InvitationTicket? ticket)
    {
        if (ticket is null)
        {
            return false;
        }

        var secretValid = !string.IsNullOrEmpty(ticket.Secret) &&
            ticket.Secret.Length <= MAX_SECRET_LENGTH &&
            ticket.Secret.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
        var keyValid = ticket.EnrollmentKey is null ||
            (ticket.EnrollmentKey.Length <= MAX_KEY_LENGTH && ticket.EnrollmentKey.TrimStart().StartsWith(PRIVATE_KEY_HEADER, StringComparison.Ordinal));
        return ticket.Id != Guid.Empty && secretValid && keyValid && Enum.IsDefined(ticket.Role) && ticket.Role != NodeRole.Relay;
    }

    /// <summary>
    /// Checks a ticket of an invitation.
    /// </summary>
    /// <param name="ticket">The ticket.</param>
    /// <exception cref="FormatException">The ticket is invalid.</exception>
    private static void ValidateTicket(InvitationTicket ticket)
    {
        if (!IsValidTicket(ticket))
        {
            throw LocalizedError.Format("Invitation.Damaged");
        }
    }

    /// <summary>
    /// Inflates the packed JSON, refusing more than <see cref="MAX_JSON_BYTES"/>.
    /// </summary>
    /// <param name="packed">The deflated bytes.</param>
    /// <returns>The JSON bytes.</returns>
    /// <exception cref="InvalidDataException">The data is not deflate or inflates too far.</exception>
    private static byte[] Inflate(byte[] packed)
    {
        using var deflate = new DeflateStream(new MemoryStream(packed), CompressionMode.Decompress);
        using var json = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = deflate.Read(chunk, 0, chunk.Length)) > 0)
        {
            if (json.Length + read > MAX_JSON_BYTES)
            {
                throw new InvalidDataException("The invitation inflates beyond the limit.");
            }

            json.Write(chunk, 0, read);
        }

        return json.ToArray();
    }
}
