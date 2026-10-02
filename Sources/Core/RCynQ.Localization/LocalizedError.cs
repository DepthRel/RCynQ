namespace RCynQ.Localization;

/// <summary>
/// Creates exceptions that carry the key of their text and its values, so the message can be worded in the language of
/// whoever reads it.
/// </summary>
/// <remarks>
/// <para>
/// A value object or a static helper that refuses a value has nobody to ask for a localizer: it is created anywhere,
/// also while a file is read. It raises its exception here instead. The <see cref="Exception.Message"/> is the English
/// text, which is what a log should hold; the key and the values travel in <see cref="Exception.Data"/>. The code that
/// shows the exception to a person asks its own localizer (<see cref="LocalizerExtensions.Describe"/>).
/// </para>
/// <para>
/// The type of the exception stays the usual one (<see cref="ArgumentException"/>, <see cref="FormatException"/>…), so
/// the code that catches it does not change.
/// </para>
/// </remarks>
public static class LocalizedError
{
    /// <summary>The entry of <see cref="Exception.Data"/> that holds the key of the text.</summary>
    private const string KEY_ENTRY = "RCynQ.Text.Key";

    /// <summary>The entry of <see cref="Exception.Data"/> that holds the values of the text.</summary>
    private const string ARGUMENTS_ENTRY = "RCynQ.Text.Arguments";

    /// <summary>
    /// The English texts, the wording of <see cref="Exception.Message"/>. They are read once and never change: the
    /// language of a user has no say here.
    /// </summary>
    private static readonly Lazy<ILocalizer> _english = new(() => new Localizer(LanguageCatalog.LoadEmbedded(), LanguageCatalog.DEFAULT_CODE));

    /// <summary>
    /// Returns the English wording of a text, the one an exception message and a log use.
    /// </summary>
    /// <param name="key">The key of the text.</param>
    /// <param name="args">
    /// The values put in place of <c>{0}</c>, <c>{1}</c>…. An exception among them stands for its message, so the cause of
    /// a failure can be passed as it is and is worded again together with the text around it.
    /// </param>
    /// <returns>The English text.</returns>
    public static string Message(string key, params object?[] args)
        => _english.Value.Format(key, [.. args.Select(arg => arg is Exception cause ? cause.Message : arg)]);

    /// <summary>
    /// Creates the exception for an argument that is refused.
    /// </summary>
    /// <param name="parameterName">The name of the refused parameter.</param>
    /// <param name="key">The key of the text.</param>
    /// <param name="args">The values of the text.</param>
    /// <returns>The exception to throw.</returns>
    public static ArgumentException Argument(string? parameterName, string key, params object?[] args)
        => Mark(new ArgumentException(Message(key, args), parameterName), key, args);

    /// <summary>
    /// Creates the exception for a text that is not in the expected form.
    /// </summary>
    /// <param name="key">The key of the text.</param>
    /// <param name="args">The values of the text.</param>
    /// <returns>The exception to throw.</returns>
    public static FormatException Format(string key, params object?[] args)
        => Mark(new FormatException(Message(key, args)), key, args);

    /// <summary>
    /// Creates an exception of any type with a text that can be worded again later.
    /// </summary>
    /// <typeparam name="TException">The type of the exception.</typeparam>
    /// <param name="factory">Creates the exception from its English message.</param>
    /// <param name="key">The key of the text.</param>
    /// <param name="args">The values of the text.</param>
    /// <returns>The exception to throw.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="factory"/> is <see langword="null"/>.</exception>
    public static TException Create<TException>(Func<string, TException> factory, string key, params object?[] args)
        where TException : Exception
    {
        ArgumentNullException.ThrowIfNull(factory);
        return Mark(factory(Message(key, args)), key, args);
    }

    /// <summary>
    /// Reads the key and the values of the text of an exception.
    /// </summary>
    /// <param name="exception">The exception.</param>
    /// <param name="key">The key of the text.</param>
    /// <param name="args">The values of the text.</param>
    /// <returns><see langword="true"/> when the exception was created here.</returns>
    public static bool TryRead(Exception? exception, out string key, out object?[] args)
    {
        if (exception?.Data[KEY_ENTRY] is string storedKey)
        {
            key = storedKey;
            args = exception.Data[ARGUMENTS_ENTRY] as object?[] ?? [];
            return true;
        }

        key = string.Empty;
        args = [];
        return false;
    }

    /// <summary>
    /// Records the key and the values of the text in an exception.
    /// </summary>
    /// <typeparam name="TException">The type of the exception.</typeparam>
    /// <param name="exception">The exception.</param>
    /// <param name="key">The key of the text.</param>
    /// <param name="args">The values of the text.</param>
    /// <returns>The same exception.</returns>
    private static TException Mark<TException>(TException exception, string key, object?[] args)
        where TException : Exception
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        exception.Data[KEY_ENTRY] = key;
        exception.Data[ARGUMENTS_ENTRY] = args;
        return exception;
    }
}
