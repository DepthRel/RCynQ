namespace RCynQ.Sync.Partial;

/// <summary>
/// One line of a text file, as a place in its bytes.
/// </summary>
/// <param name="Start">The offset of the first byte of the line.</param>
/// <param name="Length">The number of bytes of the line with its line ending.</param>
/// <param name="ContentLength">The number of bytes of the line without its line ending (<c>\n</c> or <c>\r\n</c>).</param>
/// <param name="HasLineEnd">Whether the line ends with a line ending; only the last line of a file may have none.</param>
public readonly record struct TextLine(int Start, int Length, int ContentLength, bool HasLineEnd);
