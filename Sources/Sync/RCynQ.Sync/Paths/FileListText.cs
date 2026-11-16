using RCynQ.Localization;

namespace RCynQ.Sync.Paths;

/// <summary>
/// Words a list of files for a message: one per line behind a bullet, cut short after a number of them.
/// </summary>
internal static class FileListText
{
    /// <summary>
    /// How many files a message names before it only counts the rest.
    /// </summary>
    public const int MAX_LISTED_FILES = 20;

    /// <summary>
    /// What starts the line of a file: a bullet level with the text above, so the list is neither a dash nor numbered.
    /// </summary>
    public const string BULLET = "• ";

    /// <summary>
    /// What starts the line that counts the files left out: as wide as <see cref="BULLET"/>, so it lines up with the names.
    /// </summary>
    private const string CONTINUATION = "  ";

    /// <summary>
    /// Lists files for a message, one per line behind a bullet, cutting a long list short.
    /// </summary>
    /// <param name="localizer">The localizer that words the rest of the list.</param>
    /// <param name="paths">The files.</param>
    /// <returns>The text.</returns>
    public static string Describe(ILocalizer localizer, IReadOnlyCollection<string> paths)
    {
        var shown = paths.Take(MAX_LISTED_FILES).Select(path => BULLET + path);
        var text = string.Join('\n', shown);

        return paths.Count > MAX_LISTED_FILES
            ? $"{text}\n{CONTINUATION}{localizer.Format("Git.PullMoreFiles", paths.Count - MAX_LISTED_FILES)}"
            : text;
    }
}
