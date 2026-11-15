namespace RCynQ.Sync.Partial;

/// <summary>
/// One change between two versions of a file: a run of lines of the first replaced by a run of lines of the second.
/// </summary>
/// <param name="Index">The number of the change in the file, from 0.</param>
/// <param name="BaseStart">The first replaced line of the first version, from 0.</param>
/// <param name="BaseCount">How many lines of the first version are replaced; 0 for lines only added.</param>
/// <param name="WorkStart">The first line of the second version that takes their place, from 0.</param>
/// <param name="WorkCount">How many lines of the second version take their place; 0 for lines only removed.</param>
public sealed record DiffBlock(int Index, int BaseStart, int BaseCount, int WorkStart, int WorkCount);
