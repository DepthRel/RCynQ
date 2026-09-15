namespace RCynQ.Core.Validation;

/// <summary>
/// Shared argument validation helpers used by domain records and services.
/// </summary>
/// <remarks>
/// All helpers throw standard <see cref="ArgumentException"/>-derived exceptions so
/// callers get consistent diagnostics regardless of which record rejected a value.
/// </remarks>
public static class Guard
{
    /// <summary>
    /// Ensures that a reference argument is not <see langword="null"/>.
    /// </summary>
    /// <typeparam name="T">The reference type of the argument.</typeparam>
    /// <param name="value">The argument value.</param>
    /// <param name="parameterName">The parameter name used in the exception.</param>
    /// <returns>The non-null value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public static T NotNull<T>(T? value, string parameterName)
        where T : class
        => value ?? throw new ArgumentNullException(parameterName);

    /// <summary>
    /// Ensures that a string argument is neither <see langword="null"/> nor blank and returns it trimmed.
    /// </summary>
    /// <param name="value">The argument value.</param>
    /// <param name="parameterName">The parameter name used in the exception.</param>
    /// <returns>The trimmed value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="value"/> is blank.</exception>
    public static string NotBlank(string? value, string parameterName)
    {
        if (value is null)
        {
            throw new ArgumentNullException(parameterName);
        }

        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            throw new ArgumentException("Value must not be blank.", parameterName);
        }

        return trimmed;
    }

    /// <summary>
    /// Ensures that an optional string argument is not blank when present and returns it trimmed.
    /// </summary>
    /// <param name="value">The argument value, or <see langword="null"/>.</param>
    /// <param name="parameterName">The parameter name used in the exception.</param>
    /// <returns>The trimmed value, or <see langword="null"/> when the value was <see langword="null"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="value"/> is present but blank.</exception>
    public static string? OptionalNotBlank(string? value, string parameterName)
        => value is null ? null : NotBlank(value, parameterName);

    /// <summary>
    /// Ensures that a TCP port is inside the valid range.
    /// </summary>
    /// <param name="port">The port value.</param>
    /// <param name="parameterName">The parameter name used in the exception.</param>
    /// <param name="allowZero">
    /// <see langword="true"/> to accept <c>0</c>, which means "let the operating system pick an ephemeral port".
    /// </param>
    /// <returns>The validated port.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The port is outside the accepted range.</exception>
    public static int Port(int port, string parameterName, bool allowZero = false)
    {
        var min = allowZero ? 0 : 1;
        if (port < min || port > 65535)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                port,
                $"TCP port must be in range {min}..65535.");
        }

        return port;
    }

    /// <summary>
    /// Ensures that an integer argument is non-negative.
    /// </summary>
    /// <param name="value">The argument value.</param>
    /// <param name="parameterName">The parameter name used in the exception.</param>
    /// <returns>The validated value.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is negative.</exception>
    public static int NonNegative(int value, string parameterName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Value must be non-negative.");
        }

        return value;
    }

    /// <summary>
    /// Ensures that a long integer argument is non-negative.
    /// </summary>
    /// <param name="value">The argument value.</param>
    /// <param name="parameterName">The parameter name used in the exception.</param>
    /// <returns>The validated value.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is negative.</exception>
    public static long NonNegative(long value, string parameterName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Value must be non-negative.");
        }

        return value;
    }

    /// <summary>
    /// Ensures that an enum argument is a defined member of its type.
    /// </summary>
    /// <typeparam name="TEnum">The enum type.</typeparam>
    /// <param name="value">The argument value.</param>
    /// <param name="parameterName">The parameter name used in the exception.</param>
    /// <returns>The validated value.</returns>
    /// <exception cref="ArgumentException"><paramref name="value"/> is not a defined member.</exception>
    public static TEnum Defined<TEnum>(TEnum value, string parameterName)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentException($"{typeof(TEnum).Name} value '{value}' is not defined.", parameterName);
        }

        return value;
    }

    /// <summary>
    /// Checks whether the given text is a 64 character hexadecimal SHA-256 digest.
    /// </summary>
    /// <param name="value">The text to check.</param>
    /// <returns><see langword="true"/> when the text is a well-formed digest; otherwise <see langword="false"/>.</returns>
    public static bool IsSha256Hex(string? value)
    {
        if (value is null || value.Length != 64)
        {
            return false;
        }

        foreach (var c in value)
        {
            var isHex = c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
            if (!isHex)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Ensures that the given text is a SHA-256 hexadecimal digest and returns it in lowercase.
    /// </summary>
    /// <param name="value">The digest text.</param>
    /// <param name="parameterName">The parameter name used in the exception.</param>
    /// <returns>The lowercase digest.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="value"/> is not 64 hexadecimal characters.</exception>
    public static string Sha256Hex(string? value, string parameterName)
    {
        var trimmed = NotBlank(value, parameterName);
        if (!IsSha256Hex(trimmed))
        {
            throw new ArgumentException("SHA-256 hash must be exactly 64 hexadecimal characters.", parameterName);
        }

        return trimmed.ToLowerInvariant();
    }
}
