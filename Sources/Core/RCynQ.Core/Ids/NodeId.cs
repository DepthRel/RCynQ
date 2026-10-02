using System.Text.Json.Serialization;
using RCynQ.Localization;

namespace RCynQ.Core.Ids;

/// <summary>
/// A strongly-typed identifier of an RCynQ node.
/// </summary>
/// <remarks>
/// The value is trimmed and validated on construction. Equality is ordinal and
/// case-sensitive. Only letters, digits, <c>-</c>, <c>_</c> and <c>.</c> are allowed so the
/// identifier can be used safely in HTTP headers, file names and log output.
/// The identifier is serialized to JSON as a plain string.
/// </remarks>
[JsonConverter(typeof(NodeIdJsonConverter))]
public readonly record struct NodeId
{
    /// <summary>
    /// Maximum allowed length of a node identifier value.
    /// </summary>
    public const int MAX_LENGTH = 128;

    /// <summary>
    /// Initializes a new instance of the <see cref="NodeId"/> struct.
    /// </summary>
    /// <param name="value">
    /// The node identifier. Must not be blank, must not exceed <see cref="MAX_LENGTH"/> characters and
    /// may contain only letters, digits, <c>-</c>, <c>_</c> and <c>.</c>.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="value"/> is blank, too long or contains forbidden characters.</exception>
    public NodeId(string value)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            throw LocalizedError.Argument(nameof(value), "NodeId.Blank");
        }

        if (trimmed.Length > MAX_LENGTH)
        {
            throw LocalizedError.Argument(nameof(value), "NodeId.TooLong", MAX_LENGTH);
        }

        foreach (var c in trimmed)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'))
            {
                throw LocalizedError.Argument(nameof(value), "NodeId.Forbidden");
            }
        }

        // "." and ".." pass the character check, but as a directory name they are the directory itself and its parent.
        if (trimmed.All(c => c == '.'))
        {
            throw LocalizedError.Argument(nameof(value), "NodeId.OnlyDots");
        }

        _value = trimmed;
    }

    /// <summary>
    /// Gets the normalized node identifier value.
    /// </summary>
    /// <remarks>
    /// Returns an empty string for the <see langword="default"/> instance.
    /// </remarks>
    public string Value => _value ?? string.Empty;

    /// <summary>
    /// Backing field of <see cref="Value"/>; <see langword="null"/> only for the default instance.
    /// </summary>
    private readonly string? _value;

    /// <summary>
    /// Gets a value indicating whether this instance is the uninitialized default value.
    /// </summary>
    public bool IsEmpty => string.IsNullOrEmpty(_value);

    /// <summary>
    /// Parses the given text into a <see cref="NodeId"/>.
    /// </summary>
    /// <param name="value">The node identifier text.</param>
    /// <returns>A new <see cref="NodeId"/> with the normalized value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="value"/> is not a valid identifier.</exception>
    public static NodeId Parse(string value) => new(value);

    /// <summary>
    /// Tries to parse the given text into a <see cref="NodeId"/>.
    /// </summary>
    /// <param name="value">The node identifier text, or <see langword="null"/>.</param>
    /// <param name="nodeId">When this method returns <see langword="true"/>, the parsed identifier; otherwise <see langword="default"/>.</param>
    /// <returns><see langword="true"/> when <paramref name="value"/> is a valid identifier; otherwise <see langword="false"/>.</returns>
    public static bool TryParse(string? value, out NodeId nodeId)
    {
        nodeId = default;
        if (value is null)
        {
            return false;
        }

        try
        {
            nodeId = new NodeId(value);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// Returns the node identifier value.
    /// </summary>
    /// <returns>The normalized node identifier value.</returns>
    public override string ToString() => Value;
}
