using System.Text;

namespace RCynQ.Core.Config;

/// <summary>
/// The rules of the name the nodes call a repository by.
/// </summary>
/// <remarks>
/// A name travels between the nodes and becomes part of a signed patch, so it is restricted to 1–64 ASCII letters,
/// digits, dots, underscores and hyphens, and it is never a path: it cannot be <c>.</c> or <c>..</c> and has no
/// separators.
/// </remarks>
public static class RepositoryName
{
    /// <summary>
    /// The longest name.
    /// </summary>
    public const int MAX_LENGTH = 64;

    /// <summary>
    /// The name suggested when the folder name gives nothing usable.
    /// </summary>
    public const string FALLBACK = "repository";

    /// <summary>
    /// The characters trimmed from the ends of a suggested name.
    /// </summary>
    private static readonly char[] _edges = { '-', '.' };

    /// <summary>
    /// The path separators.
    /// </summary>
    private static readonly char[] _separators = { '/', (char)92 };

    /// <summary>
    /// Tells whether a text is a valid repository name.
    /// </summary>
    /// <param name="name">The text; may be <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the text is a valid name.</returns>
    public static bool IsValid(string? name)
        => name is { Length: > 0 and <= MAX_LENGTH } &&
            name is not ("." or "..") &&
            name.All(IsAllowed);

    /// <summary>
    /// Suggests a name for the repository kept at a path: the name of its folder, with what a name may not hold replaced.
    /// </summary>
    /// <param name="path">The local path.</param>
    /// <returns>A valid name.</returns>
    public static string Suggest(string? path)
    {
        var trimmed = (path ?? string.Empty).Trim().TrimEnd(_separators);
        var folder = trimmed[(trimmed.LastIndexOfAny(_separators) + 1)..];
        var builder = new StringBuilder(folder.Length);
        foreach (var character in folder)
        {
            builder.Append(IsAllowed(character) ? character : '-');
        }

        var name = builder.ToString().Trim(_edges);
        if (name.Length > MAX_LENGTH)
        {
            name = name[..MAX_LENGTH].TrimEnd(_edges);
        }

        return IsValid(name) ? name : FALLBACK;
    }

    /// <summary>
    /// Tells whether a character may be part of a name.
    /// </summary>
    /// <param name="character">The character.</param>
    /// <returns><see langword="true"/> for ASCII letters, digits, <c>.</c>, <c>_</c> and <c>-</c>.</returns>
    private static bool IsAllowed(char character)
        => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-';
}
