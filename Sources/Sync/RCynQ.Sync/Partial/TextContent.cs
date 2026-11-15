namespace RCynQ.Sync.Partial;

/// <summary>
/// Tells text from binary content the way Git does: binary content has a zero byte near its start.
/// </summary>
public static class TextContent
{
    /// <summary>How many bytes from the start are looked at, as Git looks at them.</summary>
    private const int PROBE_BYTES = 8000;

    /// <summary>
    /// Tells whether content is binary.
    /// </summary>
    /// <param name="content">The content.</param>
    /// <returns><see langword="true"/> when a zero byte is among its first bytes.</returns>
    public static bool IsBinary(ReadOnlySpan<byte> content)
        => content[..Math.Min(content.Length, PROBE_BYTES)].Contains((byte)0);
}
