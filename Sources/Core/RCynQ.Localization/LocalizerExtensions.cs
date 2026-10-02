namespace RCynQ.Localization;

/// <summary>
/// What every <see cref="ILocalizer"/> can do on top of looking up a text.
/// </summary>
public static class LocalizerExtensions
{
    /// <summary>
    /// Words the message of an exception for a person: in the language of the localizer when the exception carries the
    /// key of its text (see <see cref="LocalizedError"/>), otherwise as the exception words it itself. An exception among
    /// the values of the text, the cause of the failure, is worded the same way.
    /// </summary>
    /// <param name="localizer">The localizer of the reader.</param>
    /// <param name="exception">The exception.</param>
    /// <returns>The message to show.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static string Describe(this ILocalizer localizer, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(exception);
        return LocalizedError.TryRead(exception, out var key, out var args)
            ? localizer.Format(key, [.. args.Select(arg => arg is Exception cause ? localizer.Describe(cause) : arg)])
            : exception.Message;
    }
}
