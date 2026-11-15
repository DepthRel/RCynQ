namespace RCynQ.Sync.Partial;

/// <summary>
/// The changes between the version of a text file the peer has and the working file, and the versions that take only some
/// of them.
/// </summary>
/// <remarks>
/// A version with a part of the changes is put together from the bytes of both files: lines outside the chosen changes come
/// from the version of the peer, lines of the chosen changes from the working file, each with its own line ending.
/// </remarks>
public sealed class FileDiff
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FileDiff"/> class.
    /// </summary>
    /// <param name="baseText">The version of the peer.</param>
    /// <param name="workText">The working file.</param>
    /// <param name="blocks">The changes.</param>
    private FileDiff(TextLines baseText, TextLines workText, IReadOnlyList<DiffBlock> blocks)
    {
        Base = baseText;
        Work = workText;
        Blocks = blocks;
    }

    /// <summary>Gets the version of the peer.</summary>
    public TextLines Base { get; }

    /// <summary>Gets the working file.</summary>
    public TextLines Work { get; }

    /// <summary>Gets the changes, in the order of the file.</summary>
    public IReadOnlyList<DiffBlock> Blocks { get; }

    /// <summary>
    /// Compares two versions of a text file.
    /// </summary>
    /// <param name="baseBytes">The version of the peer; empty when it has none.</param>
    /// <param name="workBytes">The working file.</param>
    /// <returns>The diff.</returns>
    public static FileDiff Create(byte[] baseBytes, byte[] workBytes)
    {
        ArgumentNullException.ThrowIfNull(baseBytes);
        ArgumentNullException.ThrowIfNull(workBytes);

        var baseText = TextLines.Split(baseBytes);
        var workText = TextLines.Split(workBytes);
        var identities = new LineIdentities();
        var blocks = MyersLineDiff.Compare(identities.Identify(baseText), identities.Identify(workText));
        return new FileDiff(baseText, workText, blocks);
    }

    /// <summary>
    /// Puts together the version that takes only the chosen changes.
    /// </summary>
    /// <param name="chosen">The indexes of the chosen changes; unknown ones are ignored.</param>
    /// <returns>The bytes of the version.</returns>
    public byte[] Compose(IEnumerable<int> chosen)
    {
        ArgumentNullException.ThrowIfNull(chosen);

        var taken = chosen.ToHashSet();
        using var output = new MemoryStream(Math.Max(Base.Bytes.Length, Work.Bytes.Length));
        var next = 0;
        foreach (var block in Blocks)
        {
            output.Write(Base.Range(next, block.BaseStart - next));
            output.Write(taken.Contains(block.Index) ? Work.Range(block.WorkStart, block.WorkCount) : Base.Range(block.BaseStart, block.BaseCount));
            next = block.BaseStart + block.BaseCount;
        }

        output.Write(Base.Range(next, Base.Count - next));
        return output.ToArray();
    }

    /// <summary>
    /// Finds the changes a version was put together from.
    /// </summary>
    /// <param name="content">The version.</param>
    /// <returns>The indexes of its changes, or <see langword="null"/> when the version is not made of whole changes of this diff.</returns>
    public IReadOnlyList<int>? FindChoice(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (content.AsSpan().SequenceEqual(Base.Bytes))
        {
            return Array.Empty<int>();
        }

        if (content.AsSpan().SequenceEqual(Work.Bytes))
        {
            return Blocks.Select(block => block.Index).ToArray();
        }

        // The changes of the version against the base sit where changes of the working file sit; whether the version is
        // exactly those changes is told by putting it together again.
        var other = Create(Base.Bytes, content);
        var ranges = other.Blocks.Select(block => (block.BaseStart, block.BaseCount)).ToHashSet();
        var candidate = Blocks.Where(block => ranges.Contains((block.BaseStart, block.BaseCount))).Select(block => block.Index).ToArray();
        return Compose(candidate).AsSpan().SequenceEqual(content) ? candidate : null;
    }
}
