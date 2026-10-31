using System.Text;
using System.Text.RegularExpressions;
using RCynQ.MergeTools.Enums;

namespace RCynQ.MergeTools;

/// <summary>
/// Templates of the arguments of merge tools: reading the one the user typed, checking it and filling it in.
/// </summary>
/// <remarks>
/// A template is a list of arguments with placeholders in braces: <c>{local}</c>, <c>{base}</c>, <c>{incoming}</c>,
/// <c>{result}</c> for the files and <c>{localLabel}</c>, <c>{baseLabel}</c>, <c>{incomingLabel}</c>,
/// <c>{resultLabel}</c> for the titles of the panes. A placeholder may be a whole argument or a part of one
/// (<c>/merged:{result}</c>). Each argument stays one argument whatever the path in it holds, because the tool is
/// started without a shell.
/// </remarks>
public static partial class MergeToolArguments
{
    /// <summary>The placeholder of the local version.</summary>
    public const string LOCAL = "local";

    /// <summary>The placeholder of the common ancestor.</summary>
    public const string BASE = "base";

    /// <summary>The placeholder of the incoming version.</summary>
    public const string INCOMING = "incoming";

    /// <summary>The placeholder of the result.</summary>
    public const string RESULT = "result";

    /// <summary>The placeholder of the title of the local version.</summary>
    public const string LOCAL_LABEL = "localLabel";

    /// <summary>The placeholder of the title of the common ancestor.</summary>
    public const string BASE_LABEL = "baseLabel";

    /// <summary>The placeholder of the title of the incoming version.</summary>
    public const string INCOMING_LABEL = "incomingLabel";

    /// <summary>The placeholder of the title of the result.</summary>
    public const string RESULT_LABEL = "resultLabel";

    /// <summary>Every placeholder RCynQ fills in.</summary>
    private static readonly HashSet<string> _known = new(StringComparer.Ordinal)
    {
        LOCAL, BASE, INCOMING, RESULT, LOCAL_LABEL, BASE_LABEL, INCOMING_LABEL, RESULT_LABEL
    };

    /// <summary>
    /// Splits the text the user typed into arguments: spaces separate them, double quotation marks keep spaces inside one.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The arguments, or <see langword="null"/> when a quotation mark is not closed.</returns>
    public static IReadOnlyList<string>? Parse(string? text)
    {
        var arguments = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return arguments;
        }

        var current = new StringBuilder();
        var quoted = false;
        var started = false;
        foreach (var character in text)
        {
            if (character == '"')
            {
                quoted = !quoted;
                started = true;
                continue;
            }

            if (char.IsWhiteSpace(character) && !quoted)
            {
                if (started)
                {
                    arguments.Add(current.ToString());
                    current.Clear();
                    started = false;
                }

                continue;
            }

            current.Append(character);
            started = true;
        }

        if (quoted)
        {
            return null;
        }

        if (started)
        {
            arguments.Add(current.ToString());
        }

        return arguments;
    }

    /// <summary>
    /// Writes arguments back as one line, quoting the ones that hold spaces; <see cref="Parse"/> reads it back.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <returns>The text.</returns>
    public static string Format(IEnumerable<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return string.Join(' ', arguments.Select(argument => argument.Length == 0 || argument.Any(char.IsWhiteSpace) ? $"\"{argument}\"" : argument));
    }

    /// <summary>
    /// Checks a template: every placeholder is known, and the tool gets the local and the incoming version and the place
    /// of the result.
    /// </summary>
    /// <param name="template">The template, or <see langword="null"/> when its text could not be read.</param>
    /// <returns>What is wrong, or <see cref="MergeToolTemplateProblem.None"/>.</returns>
    public static MergeToolTemplateProblem Validate(IReadOnlyList<string>? template)
    {
        if (template is null || template.Count == 0)
        {
            return MergeToolTemplateProblem.Unreadable;
        }

        var used = template.SelectMany(argument => Placeholder().Matches(argument)).Select(match => match.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        if (used.Any(name => !_known.Contains(name)))
        {
            return MergeToolTemplateProblem.UnknownPlaceholder;
        }

        if (!used.Contains(LOCAL))
        {
            return MergeToolTemplateProblem.MissingLocal;
        }

        if (!used.Contains(INCOMING))
        {
            return MergeToolTemplateProblem.MissingIncoming;
        }

        return used.Contains(RESULT) ? MergeToolTemplateProblem.None : MergeToolTemplateProblem.MissingResult;
    }

    /// <summary>
    /// Fills a template in.
    /// </summary>
    /// <param name="template">The template.</param>
    /// <param name="files">The files of the merge.</param>
    /// <param name="labels">The titles of the panes.</param>
    /// <returns>The arguments.</returns>
    /// <exception cref="ArgumentException">The template holds a placeholder RCynQ does not know.</exception>
    public static IReadOnlyList<string> Expand(IReadOnlyList<string> template, MergeToolFiles files, MergeToolLabels labels)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(labels);

        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [LOCAL] = files.Local,
            [BASE] = files.Base,
            [INCOMING] = files.Incoming,
            [RESULT] = files.Result,
            [LOCAL_LABEL] = labels.Local,
            [BASE_LABEL] = labels.Base,
            [INCOMING_LABEL] = labels.Incoming,
            [RESULT_LABEL] = labels.Result
        };

        return template
            .Select(argument => Placeholder().Replace(argument, match => values.TryGetValue(match.Groups[1].Value, out var value)
                ? value
                : throw new ArgumentException($"Unknown placeholder '{match.Value}' in the arguments of a merge tool.", nameof(template))))
            .ToList();
    }

    /// <summary>
    /// Matches a placeholder in braces and captures its name.
    /// </summary>
    /// <returns>The expression.</returns>
    [GeneratedRegex(@"\{([A-Za-z]+)\}", RegexOptions.CultureInvariant)]
    private static partial Regex Placeholder();
}
