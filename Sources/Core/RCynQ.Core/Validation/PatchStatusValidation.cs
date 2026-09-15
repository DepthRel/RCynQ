using RCynQ.Core.Enums;

namespace RCynQ.Core.Validation;

/// <summary>
/// Provides validation and parsing helpers for the <see cref="PatchStatus"/> enum.
/// </summary>
public static class PatchStatusValidation
{
    /// <summary>
    /// Checks whether the given value is a defined <see cref="PatchStatus"/> member.
    /// </summary>
    /// <param name="value">The value to check.</param>
    /// <returns><see langword="true"/> when the value is defined; otherwise <see langword="false"/>.</returns>
    public static bool IsDefined(PatchStatus value)
        => EnumValidationCore.IsDefined(value);

    /// <summary>
    /// Parses the given text into a <see cref="PatchStatus"/>.
    /// </summary>
    /// <param name="value">
    /// The status name, such as <c>"applied"</c> or <c>"conflictpending"</c>.
    /// </param>
    /// <param name="ignoreCase">
    /// <see langword="true"/> to ignore case; otherwise <see langword="false"/>.
    /// Defaults to <see langword="true"/>.
    /// </param>
    /// <returns>The parsed <see cref="PatchStatus"/>.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="value"/> is null, blank, or does not match a defined member.
    /// </exception>
    public static PatchStatus Parse(string? value, bool ignoreCase = true)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Patch status name must not be blank.", nameof(value));
        }

        if (!EnumValidationCore.TryParseByName(value, ignoreCase, out PatchStatus result))
        {
            throw new ArgumentException(
                $"Value '{value}' is not a valid PatchStatus.",
                nameof(value));
        }

        return result;
    }

    /// <summary>
    /// Tries to parse the given text into a <see cref="PatchStatus"/>.
    /// </summary>
    /// <param name="value">The status name, or <see langword="null"/>.</param>
    /// <param name="ignoreCase">
    /// <see langword="true"/> to ignore case; otherwise <see langword="false"/>.
    /// </param>
    /// <param name="status">
    /// When this method returns <see langword="true"/>, the parsed status;
    /// otherwise <see langword="default"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the value is a defined member name;
    /// otherwise <see langword="false"/>.
    /// </returns>
    public static bool TryParse(string? value, bool ignoreCase, out PatchStatus status)
        => EnumValidationCore.TryParseByName(value, ignoreCase, out status);
}
