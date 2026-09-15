using RCynQ.Core.Enums;

namespace RCynQ.Core.Validation;

/// <summary>
/// Provides validation and parsing helpers for the <see cref="TransportKind"/> enum.
/// </summary>
public static class TransportKindValidation
{
    /// <summary>
    /// Checks whether the given value is a defined <see cref="TransportKind"/> member.
    /// </summary>
    /// <param name="value">The value to check.</param>
    /// <returns><see langword="true"/> when the value is defined; otherwise <see langword="false"/>.</returns>
    public static bool IsDefined(TransportKind value)
        => EnumValidationCore.IsDefined(value);

    /// <summary>
    /// Parses the given text into a <see cref="TransportKind"/>.
    /// </summary>
    /// <param name="value">The transport kind name, such as <c>"direct"</c> or <c>"amneziawg"</c>.</param>
    /// <param name="ignoreCase">
    /// <see langword="true"/> to ignore case; otherwise <see langword="false"/>.
    /// Defaults to <see langword="true"/>.
    /// </param>
    /// <returns>The parsed <see cref="TransportKind"/>.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="value"/> is null, blank, or does not match a defined member.
    /// </exception>
    public static TransportKind Parse(string? value, bool ignoreCase = true)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Transport kind name must not be blank.", nameof(value));
        }

        if (!EnumValidationCore.TryParseByName(value, ignoreCase, out TransportKind result))
        {
            throw new ArgumentException(
                $"Value '{value}' is not a valid TransportKind.",
                nameof(value));
        }

        return result;
    }

    /// <summary>
    /// Tries to parse the given text into a <see cref="TransportKind"/>.
    /// </summary>
    /// <param name="value">The transport kind name, or <see langword="null"/>.</param>
    /// <param name="ignoreCase">
    /// <see langword="true"/> to ignore case; otherwise <see langword="false"/>.
    /// </param>
    /// <param name="kind">
    /// When this method returns <see langword="true"/>, the parsed kind;
    /// otherwise <see cref="TransportKind.Direct"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the value is a defined member name;
    /// otherwise <see langword="false"/>.
    /// </returns>
    public static bool TryParse(string? value, bool ignoreCase, out TransportKind kind)
        => EnumValidationCore.TryParseByName(value, ignoreCase, out kind);
}
