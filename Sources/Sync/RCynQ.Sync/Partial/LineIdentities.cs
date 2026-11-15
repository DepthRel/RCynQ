namespace RCynQ.Sync.Partial;

/// <summary>
/// Gives every distinct line one number, so lines are compared as numbers. Two lines are the same when their text is the
/// same and both have a line ending or both have none: <c>\r\n</c> and <c>\n</c> do not make a difference.
/// </summary>
public sealed class LineIdentities
{
    /// <summary>The number of every distinct line.</summary>
    private readonly Dictionary<LineKey, int> _numbers = new(new LineKeyComparer());

    /// <summary>
    /// Numbers the lines of a file.
    /// </summary>
    /// <param name="text">The lines of the file.</param>
    /// <returns>The number of each line, in the order of the file.</returns>
    public int[] Identify(TextLines text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var numbers = new int[text.Count];
        for (var index = 0; index < text.Count; index++)
        {
            var line = text.Lines[index];
            var key = new LineKey(text.Bytes, line.Start, line.ContentLength, line.HasLineEnd);
            if (!_numbers.TryGetValue(key, out var number))
            {
                number = _numbers.Count;
                _numbers.Add(key, number);
            }

            numbers[index] = number;
        }

        return numbers;
    }

    /// <summary>
    /// A line as its text in the bytes of its file and whether it has a line ending.
    /// </summary>
    /// <param name="Bytes">The bytes of the file.</param>
    /// <param name="Start">The offset of the line.</param>
    /// <param name="Length">The length of its text.</param>
    /// <param name="HasLineEnd">Whether it has a line ending.</param>
    private readonly record struct LineKey(byte[] Bytes, int Start, int Length, bool HasLineEnd)
    {
        /// <summary>Gets the text of the line.</summary>
        public ReadOnlySpan<byte> Text => Bytes.AsSpan(Start, Length);
    }

    /// <summary>
    /// Compares lines by their text and line ending, not by the file they are in.
    /// </summary>
    private sealed class LineKeyComparer : IEqualityComparer<LineKey>
    {
        /// <inheritdoc />
        public bool Equals(LineKey x, LineKey y) => x.HasLineEnd == y.HasLineEnd && x.Text.SequenceEqual(y.Text);

        /// <inheritdoc />
        public int GetHashCode(LineKey obj)
        {
            var hash = new HashCode();
            hash.AddBytes(obj.Text);
            hash.Add(obj.HasLineEnd);
            return hash.ToHashCode();
        }
    }
}
