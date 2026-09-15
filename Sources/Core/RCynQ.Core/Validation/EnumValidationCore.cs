namespace RCynQ.Core.Validation;

/// <summary>
/// Provides shared internal enum parsing and definedness checks.
/// </summary>
internal static class EnumValidationCore
{
    /// <summary>
    /// Checks whether the given value is a defined member of the enum type.
    /// </summary>
    /// <typeparam name="TEnum">The enum type.</typeparam>
    /// <param name="value">The value to check.</param>
    /// <returns><see langword="true"/> when the value is defined; otherwise <see langword="false"/>.</returns>
    public static bool IsDefined<TEnum>(TEnum value) where TEnum : struct, Enum
        => Enum.IsDefined(value);

    /// <summary>
    /// Finds a defined enum member by name.
    /// </summary>
    /// <typeparam name="TEnum">The enum type.</typeparam>
    /// <param name="name">The member name to find, or <see langword="null"/>.</param>
    /// <param name="ignoreCase">
    /// <see langword="true"/> to ignore case; otherwise <see langword="false"/>.
    /// </param>
    /// <param name="result">
    /// When this method returns <see langword="true"/>, the matching enum member.
    /// Otherwise the default value of <typeparamref name="TEnum"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when a defined member with the given name exists;
    /// otherwise <see langword="false"/>.
    /// </returns>
    public static bool TryParseByName<TEnum>(string? name, bool ignoreCase, out TEnum result)
        where TEnum : struct, Enum
        => Enum.TryParse<TEnum>(name, ignoreCase, out result);
}
