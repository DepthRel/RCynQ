using System.Globalization;
using System.Text;

namespace RCynQ.Localization;

/// <summary>
/// A message that is stored or sent as the key of its text and its values, and worded only when it is shown. A message
/// kept in the database in Russian would stay Russian for a user who later switches to English; kept as a key it follows
/// the language of whoever reads it.
/// </summary>
/// <remarks>
/// The message is a plain string, so it fits the text columns and fields that already exist. An encoded message starts
/// with a marker that no ordinary text has; a string without the marker (a message stored by an earlier version, or a
/// text that came from the system) is shown as it is.
/// </remarks>
public static class LocalizedText
{
    /// <summary>
    /// The character that separates the parts of an encoded message: the ASCII "unit separator", which does not occur in
    /// ordinary text.
    /// </summary>
    private const char Separator = '\u001F';

    /// <summary>
    /// The marker an encoded message starts with.
    /// </summary>
    private static readonly string Marker = $"{Separator}lt{Separator}";

    /// <summary>
    /// Encodes a message.
    /// </summary>
    /// <param name="key">The key of the text.</param>
    /// <param name="args">The values put in place of <c>{0}</c>, <c>{1}</c>…; they are stored as invariant text.</param>
    /// <returns>The encoded message.</returns>
    public static string Encode(string key, params object?[] args)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var text = new StringBuilder(Marker).Append(key.Replace(Separator, ' '));
        foreach (var arg in args)
        {
            text.Append(Separator).Append(Convert.ToString(arg, CultureInfo.InvariantCulture)?.Replace(Separator, ' '));
        }

        return text.ToString();
    }

    /// <summary>
    /// Tells whether a string is an encoded message.
    /// </summary>
    /// <param name="text">The string.</param>
    /// <returns><see langword="true"/> for a message made by <see cref="Encode"/>.</returns>
    public static bool IsEncoded(string? text) => text is not null && text.StartsWith(Marker, StringComparison.Ordinal);

    /// <summary>
    /// Words a message in the language in use.
    /// </summary>
    /// <param name="text">An encoded message, a plain text or <see langword="null"/>.</param>
    /// <param name="localizer">The localizer to use; the one of the application by default.</param>
    /// <returns>The text in the language of the localizer; a plain text is returned as it is.</returns>
    public static string? Render(string? text, ILocalizer? localizer = null)
    {
        if (!IsEncoded(text))
        {
            return text;
        }

        var parts = text![Marker.Length..].Split(Separator);
        return (localizer ?? Texts.Localizer).Format(parts[0], [.. parts.Skip(1)]);
    }
}
