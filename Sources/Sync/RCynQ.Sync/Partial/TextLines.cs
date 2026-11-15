using System.Text;

namespace RCynQ.Sync.Partial;

/// <summary>
/// The bytes of a text file split into lines; each line keeps its own line ending, so a file put together from lines
/// has the line endings it was made of.
/// </summary>
public sealed class TextLines
{
    /// <summary>The byte order mark of UTF-8.</summary>
    private static readonly byte[] _utf8Bom = { 0xEF, 0xBB, 0xBF };

    /// <summary>
    /// Initializes a new instance of the <see cref="TextLines"/> class.
    /// </summary>
    /// <param name="bytes">The bytes of the file.</param>
    /// <param name="lines">The lines.</param>
    private TextLines(byte[] bytes, IReadOnlyList<TextLine> lines)
    {
        Bytes = bytes;
        Lines = lines;
    }

    /// <summary>Gets the bytes of the file.</summary>
    public byte[] Bytes { get; }

    /// <summary>Gets the lines, in the order of the file.</summary>
    public IReadOnlyList<TextLine> Lines { get; }

    /// <summary>Gets the number of lines.</summary>
    public int Count => Lines.Count;

    /// <summary>
    /// Splits bytes into lines at every <c>\n</c>; a <c>\r</c> right before it belongs to the line ending.
    /// </summary>
    /// <param name="bytes">The bytes of the file.</param>
    /// <returns>The lines; none for an empty file.</returns>
    public static TextLines Split(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        var lines = new List<TextLine>();
        var start = 0;
        for (var index = 0; index < bytes.Length; index++)
        {
            if (bytes[index] != (byte)'\n')
            {
                continue;
            }

            var content = index - start;
            if (content > 0 && bytes[index - 1] == (byte)'\r')
            {
                content--;
            }

            lines.Add(new TextLine(start, index + 1 - start, content, true));
            start = index + 1;
        }

        if (start < bytes.Length)
        {
            lines.Add(new TextLine(start, bytes.Length - start, bytes.Length - start, false));
        }

        return new TextLines(bytes, lines);
    }

    /// <summary>
    /// Gets the bytes of a line without its line ending.
    /// </summary>
    /// <param name="index">The index of the line.</param>
    /// <returns>The bytes.</returns>
    public ReadOnlySpan<byte> Content(int index)
    {
        var line = Lines[index];
        return Bytes.AsSpan(line.Start, line.ContentLength);
    }

    /// <summary>
    /// Gets the bytes of a run of lines with their line endings.
    /// </summary>
    /// <param name="start">The index of the first line.</param>
    /// <param name="count">The number of lines.</param>
    /// <returns>The bytes; empty for no lines.</returns>
    public ReadOnlySpan<byte> Range(int start, int count)
    {
        if (count == 0)
        {
            return ReadOnlySpan<byte>.Empty;
        }

        var first = Lines[start];
        var last = Lines[start + count - 1];
        return Bytes.AsSpan(first.Start, last.Start + last.Length - first.Start);
    }

    /// <summary>
    /// Gets the text of a line for the eyes of the user: UTF-8, without its line ending and without the byte order mark of
    /// the first line.
    /// </summary>
    /// <param name="index">The index of the line.</param>
    /// <returns>The text.</returns>
    public string Text(int index)
    {
        var content = Content(index);
        if (index == 0 && content.StartsWith(_utf8Bom))
        {
            content = content[_utf8Bom.Length..];
        }

        return Encoding.UTF8.GetString(content);
    }
}
