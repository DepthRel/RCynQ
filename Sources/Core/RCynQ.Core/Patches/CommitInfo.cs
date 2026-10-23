using RCynQ.Core.Validation;

namespace RCynQ.Core.Patches;

/// <summary>
/// One commit of a delivery of commits, as the user sees it in the list and in the history.
/// </summary>
public sealed record CommitInfo
{
    /// <summary>
    /// The longest subject that is kept.
    /// </summary>
    public const int MAX_SUBJECT_LENGTH = 1000;

    /// <summary>
    /// The longest author text that is kept.
    /// </summary>
    public const int MAX_AUTHOR_LENGTH = 300;

    /// <summary>
    /// Initializes a new instance of the <see cref="CommitInfo"/> record.
    /// </summary>
    /// <param name="hash">The full commit name.</param>
    /// <param name="subject">The first line of the commit message.</param>
    /// <param name="author">The author as <c>Name &lt;email&gt;</c>.</param>
    /// <param name="authoredAtUtc">The author date.</param>
    /// <exception cref="ArgumentException">The hash is not a full commit name, or a text is too long or holds control characters.</exception>
    public CommitInfo(string hash, string subject, string author, DateTimeOffset authoredAtUtc)
    {
        if (!GitNames.IsCommit(hash))
        {
            throw new ArgumentException($"'{hash}' is not a full commit name.", nameof(hash));
        }

        Hash = hash;
        Subject = ValidateText(subject, MAX_SUBJECT_LENGTH, nameof(subject));
        Author = ValidateText(author, MAX_AUTHOR_LENGTH, nameof(author));
        AuthoredAtUtc = authoredAtUtc.ToUniversalTime();
    }

    /// <summary>
    /// Gets the full commit name.
    /// </summary>
    public string Hash { get; }

    /// <summary>
    /// Gets the first line of the commit message.
    /// </summary>
    public string Subject { get; }

    /// <summary>
    /// Gets the author as <c>Name &lt;email&gt;</c>.
    /// </summary>
    public string Author { get; }

    /// <summary>
    /// Gets the author date in UTC.
    /// </summary>
    public DateTimeOffset AuthoredAtUtc { get; }

    /// <summary>
    /// Gets the short form of the commit name shown to the user.
    /// </summary>
    public string ShortHash => Hash[..10];

    /// <summary>
    /// Checks a text of the commit that may come over the network.
    /// </summary>
    /// <param name="value">The text.</param>
    /// <param name="maxLength">The longest allowed text.</param>
    /// <param name="parameterName">The parameter name used in the exception.</param>
    /// <returns>The text.</returns>
    /// <exception cref="ArgumentException">The text is too long or holds control characters.</exception>
    private static string ValidateText(string? value, int maxLength, string parameterName)
    {
        var text = value ?? string.Empty;
        if (text.Length > maxLength || text.Any(char.IsControl))
        {
            throw new ArgumentException($"The text is longer than {maxLength} characters or holds control characters.", parameterName);
        }

        return text;
    }
}
