using RCynQ.Core.Enums;

namespace RCynQ.Core.Validation;

/// <summary>
/// Provides validation and parsing helpers for the <see cref="NodeRole"/> enum.
/// </summary>
public static class NodeRoleValidation
{
    /// <summary>
    /// Checks whether the given value is a defined <see cref="NodeRole"/> member.
    /// </summary>
    /// <param name="value">The value to check.</param>
    /// <returns><see langword="true"/> when the value is defined; otherwise <see langword="false"/>.</returns>
    public static bool IsDefined(NodeRole value)
        => EnumValidationCore.IsDefined(value);

    /// <summary>
    /// Parses the given text into a <see cref="NodeRole"/>.
    /// </summary>
    /// <param name="value">The role name, such as <c>"source"</c> or <c>"mediator"</c>.</param>
    /// <param name="ignoreCase">
    /// <see langword="true"/> to ignore case; otherwise <see langword="false"/>.
    /// Defaults to <see langword="true"/>.
    /// </param>
    /// <returns>The parsed <see cref="NodeRole"/>.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="value"/> is null, blank, or does not match a defined member.
    /// </exception>
    public static NodeRole Parse(string? value, bool ignoreCase = true)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Node role name must not be blank.", nameof(value));
        }

        if (!EnumValidationCore.TryParseByName(value, ignoreCase, out NodeRole result))
        {
            throw new ArgumentException(
                $"Value '{value}' is not a valid NodeRole.",
                nameof(value));
        }

        return result;
    }

    /// <summary>
    /// Tries to parse the given text into a <see cref="NodeRole"/>.
    /// </summary>
    /// <param name="value">The role name, or <see langword="null"/>.</param>
    /// <param name="ignoreCase">
    /// <see langword="true"/> to ignore case; otherwise <see langword="false"/>.
    /// </param>
    /// <param name="role">
    /// When this method returns <see langword="true"/>, the parsed role;
    /// otherwise <see cref="NodeRole.Source"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the value is a defined member name;
    /// otherwise <see langword="false"/>.
    /// </returns>
    public static bool TryParse(string? value, bool ignoreCase, out NodeRole role)
        => EnumValidationCore.TryParseByName(value, ignoreCase, out role);
}
