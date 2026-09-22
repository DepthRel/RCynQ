using System.Text;
using System.Text.RegularExpressions;

namespace RCynQ.Git.Manifest;

/// <summary>
/// The rules of one <c>.gitignore</c> file, evaluated against paths relative to the directory that holds the file.
/// </summary>
/// <remarks>
/// <para>
/// Implements the pattern language of <c>gitignore</c>: comments, blank lines, <c>!</c> negation, a leading <c>/</c> or a
/// slash inside the pattern (anchored to the directory of the file), a trailing <c>/</c> (directories only), <c>*</c>, <c>?</c>,
/// bracket classes, backslash escapes and the <c>**</c> forms <c>**/x</c>, <c>x/**</c> and <c>x/**/y</c>. Within a file the last matching
/// line wins.
/// </para>
/// <para>
/// Not covered: <c>.git/info/exclude</c> and the global ignore file; they are per-computer settings that the peer would not
/// share, so they cannot decide what belongs in a patch.
/// </para>
/// </remarks>
public sealed class GitIgnoreRules
{
    /// <summary>
    /// The maximum time a single pattern may take to match, so a hostile pattern cannot stall a scan.
    /// </summary>
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

    /// <summary>
    /// The parsed lines in file order.
    /// </summary>
    private readonly IReadOnlyList<Rule> _rules;

    /// <summary>
    /// Initializes a new instance of the <see cref="GitIgnoreRules"/> class.
    /// </summary>
    /// <param name="rules">The parsed lines in file order.</param>
    private GitIgnoreRules(IReadOnlyList<Rule> rules)
    {
        _rules = rules;
    }

    /// <summary>
    /// Gets a value indicating whether the file holds no rule.
    /// </summary>
    public bool IsEmpty => _rules.Count == 0;

    /// <summary>
    /// Parses the text of a <c>.gitignore</c> file.
    /// </summary>
    /// <param name="text">The file text.</param>
    /// <param name="ignoreCase">Whether names are compared without regard to case (the Git default on Windows and macOS).</param>
    /// <returns>The rules.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static GitIgnoreRules Parse(string text, bool ignoreCase)
    {
        ArgumentNullException.ThrowIfNull(text);
        var rules = new List<Rule>();
        foreach (var line in text.Split('\n'))
        {
            var rule = ParseLine(line.TrimEnd('\r'), ignoreCase);
            if (rule is not null)
            {
                rules.Add(rule);
            }
        }

        return new GitIgnoreRules(rules);
    }

    /// <summary>
    /// Decides what the file says about a path.
    /// </summary>
    /// <param name="relativePath">The path relative to the directory of the file, with <c>/</c> separators.</param>
    /// <param name="isDirectory">Whether the path is a directory (a symbolic link is not).</param>
    /// <returns><see langword="true"/> when the path is ignored, <see langword="false"/> when a <c>!</c> rule re-includes it, <see langword="null"/> when no rule matches.</returns>
    public bool? Match(string relativePath, bool isDirectory)
    {
        ArgumentNullException.ThrowIfNull(relativePath);
        var name = relativePath[(relativePath.LastIndexOf('/') + 1)..];
        for (var i = _rules.Count - 1; i >= 0; i--)
        {
            var rule = _rules[i];
            if (rule.DirectoryOnly && !isDirectory)
            {
                continue;
            }

            if (rule.Pattern.IsMatch(rule.MatchesFullPath ? relativePath : name))
            {
                return !rule.Negated;
            }
        }

        return null;
    }

    /// <summary>
    /// Parses one line.
    /// </summary>
    /// <param name="line">The line without its line break.</param>
    /// <param name="ignoreCase">Whether names are compared without regard to case.</param>
    /// <returns>The rule, or <see langword="null"/> for a blank line or a comment.</returns>
    private static Rule? ParseLine(string line, bool ignoreCase)
    {
        var text = TrimTrailingSpaces(line);
        if (text.Length == 0 || text[0] == '#')
        {
            return null;
        }

        var negated = false;
        if (text[0] == '!')
        {
            negated = true;
            text = text[1..];
        }

        var directoryOnly = false;
        if (text.EndsWith('/'))
        {
            directoryOnly = true;
            text = text.TrimEnd('/');
        }

        var matchesFullPath = text.Contains('/');
        if (text.StartsWith('/'))
        {
            text = text[1..];
        }

        if (text.Length == 0)
        {
            return null;
        }

        var options = RegexOptions.CultureInvariant | (ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None);
        return new Rule(new Regex(Translate(text), options, MatchTimeout), negated, directoryOnly, matchesFullPath);
    }

    /// <summary>
    /// Removes trailing spaces that are not escaped with a backslash.
    /// </summary>
    /// <param name="line">The line.</param>
    /// <returns>The line without unescaped trailing spaces.</returns>
    private static string TrimTrailingSpaces(string line)
    {
        var end = line.Length;
        while (end > 0 && line[end - 1] == ' ')
        {
            var backslashes = 0;
            for (var i = end - 2; i >= 0 && line[i] == '\\'; i--)
            {
                backslashes++;
            }

            if (backslashes % 2 == 1)
            {
                break;
            }

            end--;
        }

        return line[..end];
    }

    /// <summary>
    /// Translates a glob into an anchored regular expression.
    /// </summary>
    /// <param name="glob">The pattern without <c>!</c>, leading <c>/</c> and trailing <c>/</c>.</param>
    /// <returns>The regular expression text.</returns>
    private static string Translate(string glob)
    {
        var builder = new StringBuilder("^");
        var i = 0;
        if (glob.StartsWith("**/", StringComparison.Ordinal))
        {
            builder.Append("(?:.*/)?");
            i = 3;
        }

        while (i < glob.Length)
        {
            var c = glob[i];
            switch (c)
            {
                case '*':
                    i = AppendStar(builder, glob, i);
                    break;
                case '?':
                    builder.Append("[^/]");
                    i++;
                    break;
                case '[':
                    i = AppendClass(builder, glob, i);
                    break;
                case '\\' when i + 1 < glob.Length:
                    builder.Append(Regex.Escape(glob[i + 1].ToString()));
                    i += 2;
                    break;
                default:
                    builder.Append(Regex.Escape(c.ToString()));
                    i++;
                    break;
            }
        }

        return builder.Append('$').ToString();
    }

    /// <summary>
    /// Appends a run of stars.
    /// </summary>
    /// <param name="builder">The regular expression under construction.</param>
    /// <param name="glob">The pattern.</param>
    /// <param name="index">The index of the first star.</param>
    /// <returns>The index after the run.</returns>
    private static int AppendStar(StringBuilder builder, string glob, int index)
    {
        var end = index;
        while (end < glob.Length && glob[end] == '*')
        {
            end++;
        }

        var wholeSegment = end - index >= 2 && (index == 0 || glob[index - 1] == '/') && (end == glob.Length || glob[end] == '/');
        if (!wholeSegment)
        {
            builder.Append("[^/]*");
            return end;
        }

        if (end == glob.Length)
        {
            // "x/**": everything below x.
            builder.Append(".*");
            return end;
        }

        // "x/**/y": zero or more directories in between; the slash after the stars is consumed here.
        builder.Append("(?:.*/)?");
        return end + 1;
    }

    /// <summary>
    /// Appends a bracket class, or a literal <c>[</c> when the class is not closed.
    /// </summary>
    /// <param name="builder">The regular expression under construction.</param>
    /// <param name="glob">The pattern.</param>
    /// <param name="index">The index of the opening bracket.</param>
    /// <returns>The index after the class.</returns>
    private static int AppendClass(StringBuilder builder, string glob, int index)
    {
        var i = index + 1;
        var negated = i < glob.Length && (glob[i] == '!' || glob[i] == '^');
        if (negated)
        {
            i++;
        }

        var content = new StringBuilder();
        var first = true;
        while (i < glob.Length && (glob[i] != ']' || first))
        {
            var c = glob[i];
            if (c == '\\' && i + 1 < glob.Length)
            {
                content.Append('\\').Append(glob[i + 1]);
                i += 2;
            }
            else
            {
                content.Append(c is '\\' or '[' or '^' ? "\\" + c : c.ToString());
                i++;
            }

            first = false;
        }

        if (i >= glob.Length)
        {
            builder.Append("\\[");
            return index + 1;
        }

        builder.Append(negated ? "[^/" : "[").Append(content).Append(']');
        return i + 1;
    }

    /// <summary>
    /// One parsed line.
    /// </summary>
    /// <param name="Pattern">The compiled pattern.</param>
    /// <param name="Negated">Whether the line starts with <c>!</c>.</param>
    /// <param name="DirectoryOnly">Whether the line ends with <c>/</c>.</param>
    /// <param name="MatchesFullPath">Whether the pattern is matched against the whole relative path instead of the name.</param>
    private sealed record Rule(Regex Pattern, bool Negated, bool DirectoryOnly, bool MatchesFullPath);
}
