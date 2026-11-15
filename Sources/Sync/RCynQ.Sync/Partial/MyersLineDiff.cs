namespace RCynQ.Sync.Partial;

/// <summary>
/// Finds the changes between two sequences of lines with the algorithm of Eugene Myers ("An O(ND) Difference Algorithm and
/// Its Variations", 1986): the shortest list of removed and added lines, grouped into runs.
/// </summary>
/// <remarks>
/// The lines both sequences start and end with are taken off first, which leaves a small middle for the usual edit. A
/// middle that needs more than <see cref="MAX_EDITS"/> removed and added lines is one change: the search would take too long
/// and too much memory, and such a rewrite is chosen as a whole anyway. The result depends on the input only.
/// </remarks>
public static class MyersLineDiff
{
    /// <summary>The most removed and added lines searched for before the middle counts as one change.</summary>
    public const int MAX_EDITS = 2000;

    /// <summary>
    /// Finds the changes that turn the first sequence into the second.
    /// </summary>
    /// <param name="first">The numbers of the lines of the first version (see <see cref="LineIdentities"/>).</param>
    /// <param name="second">The numbers of the lines of the second version.</param>
    /// <param name="maxEdits">The most removed and added lines searched for.</param>
    /// <returns>The changes, in the order of the file, numbered from 0.</returns>
    public static IReadOnlyList<DiffBlock> Compare(IReadOnlyList<int> first, IReadOnlyList<int> second, int maxEdits = MAX_EDITS)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        ArgumentOutOfRangeException.ThrowIfNegative(maxEdits);

        var prefix = 0;
        while (prefix < first.Count && prefix < second.Count && first[prefix] == second[prefix])
        {
            prefix++;
        }

        var suffix = 0;
        while (suffix < first.Count - prefix && suffix < second.Count - prefix && first[first.Count - 1 - suffix] == second[second.Count - 1 - suffix])
        {
            suffix++;
        }

        var middle = new Middle(first, second, prefix, first.Count - prefix - suffix, second.Count - prefix - suffix);
        var matches = middle.Matches(maxEdits);

        return ToBlocks(matches, prefix, middle.FirstCount, middle.SecondCount);
    }

    /// <summary>
    /// Turns the matched lines of the middle into the runs between them.
    /// </summary>
    /// <param name="matches">The matched pairs of the middle, in ascending order, or <see langword="null"/> when the middle is one change.</param>
    /// <param name="offset">Where the middle starts in both versions.</param>
    /// <param name="firstCount">The number of lines of the first version in the middle.</param>
    /// <param name="secondCount">The number of lines of the second version in the middle.</param>
    /// <returns>The changes.</returns>
    private static IReadOnlyList<DiffBlock> ToBlocks(IReadOnlyList<(int First, int Second)>? matches, int offset, int firstCount, int secondCount)
    {
        var blocks = new List<DiffBlock>();
        var nextFirst = 0;
        var nextSecond = 0;
        foreach (var (matchFirst, matchSecond) in matches ?? Array.Empty<(int, int)>())
        {
            if (matchFirst > nextFirst || matchSecond > nextSecond)
            {
                blocks.Add(new DiffBlock(blocks.Count, offset + nextFirst, matchFirst - nextFirst, offset + nextSecond, matchSecond - nextSecond));
            }

            nextFirst = matchFirst + 1;
            nextSecond = matchSecond + 1;
        }

        if (nextFirst < firstCount || nextSecond < secondCount)
        {
            blocks.Add(new DiffBlock(blocks.Count, offset + nextFirst, firstCount - nextFirst, offset + nextSecond, secondCount - nextSecond));
        }

        return blocks;
    }

    /// <summary>
    /// The part of both versions between the lines they start and end with.
    /// </summary>
    private sealed class Middle
    {
        /// <summary>The lines of the first version.</summary>
        private readonly IReadOnlyList<int> _first;

        /// <summary>The lines of the second version.</summary>
        private readonly IReadOnlyList<int> _second;

        /// <summary>Where the middle starts in both versions.</summary>
        private readonly int _offset;

        /// <summary>
        /// Initializes a new instance of the <see cref="Middle"/> class.
        /// </summary>
        /// <param name="first">The lines of the first version.</param>
        /// <param name="second">The lines of the second version.</param>
        /// <param name="offset">Where the middle starts in both versions.</param>
        /// <param name="firstCount">The number of lines of the first version in the middle.</param>
        /// <param name="secondCount">The number of lines of the second version in the middle.</param>
        public Middle(IReadOnlyList<int> first, IReadOnlyList<int> second, int offset, int firstCount, int secondCount)
        {
            _first = first;
            _second = second;
            _offset = offset;
            FirstCount = firstCount;
            SecondCount = secondCount;
        }

        /// <summary>Gets the number of lines of the first version in the middle.</summary>
        public int FirstCount { get; }

        /// <summary>Gets the number of lines of the second version in the middle.</summary>
        public int SecondCount { get; }

        /// <summary>
        /// Finds the lines both versions keep in the middle.
        /// </summary>
        /// <param name="maxEdits">The most removed and added lines searched for.</param>
        /// <returns>The matched pairs relative to the middle, in ascending order; <see langword="null"/> when the middle is one change.</returns>
        public IReadOnlyList<(int First, int Second)>? Matches(int maxEdits)
        {
            if (FirstCount == 0 || SecondCount == 0)
            {
                return Array.Empty<(int, int)>();
            }

            var trace = Search(maxEdits);
            if (trace is null)
            {
                return null;
            }

            return Backtrack(trace);
        }

        /// <summary>
        /// Runs the greedy search, keeping what every step started from.
        /// </summary>
        /// <param name="maxEdits">The most removed and added lines searched for.</param>
        /// <returns>
        /// For every step <c>d</c> the furthest reach on the diagonals <c>-d-1 … d+1</c> before the step, or
        /// <see langword="null"/> when more edits than allowed are needed.
        /// </returns>
        private List<int[]>? Search(int maxEdits)
        {
            var max = FirstCount + SecondCount;
            var limit = Math.Min(max, maxEdits);
            var reach = new int[(2 * max) + 3];
            var center = max + 1;
            var trace = new List<int[]>();

            for (var d = 0; d <= limit; d++)
            {
                trace.Add(reach.AsSpan(center - d - 1, (2 * d) + 3).ToArray());
                for (var k = -d; k <= d; k += 2)
                {
                    var x = k == -d || (k != d && reach[center + k - 1] < reach[center + k + 1])
                        ? reach[center + k + 1]
                        : reach[center + k - 1] + 1;
                    var y = x - k;
                    while (x < FirstCount && y < SecondCount && _first[_offset + x] == _second[_offset + y])
                    {
                        x++;
                        y++;
                    }

                    reach[center + k] = x;
                    if (x >= FirstCount && y >= SecondCount)
                    {
                        return trace;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Walks the search back from the end and collects the lines that stayed.
        /// </summary>
        /// <param name="trace">What every step started from (see <see cref="Search"/>).</param>
        /// <returns>The matched pairs relative to the middle, in ascending order.</returns>
        private List<(int First, int Second)> Backtrack(List<int[]> trace)
        {
            var matches = new List<(int First, int Second)>();
            var x = FirstCount;
            var y = SecondCount;
            for (var d = trace.Count - 1; d >= 0; d--)
            {
                var before = trace[d];
                var center = d + 1;
                var k = x - y;
                var previousK = k == -d || (k != d && before[center + k - 1] < before[center + k + 1]) ? k + 1 : k - 1;
                var previousX = d == 0 ? 0 : before[center + previousK];
                var previousY = previousX - previousK;
                if (d == 0)
                {
                    previousX = 0;
                    previousY = 0;
                }

                // The run of kept lines after the edit of this step.
                var snakeStartX = d == 0 ? 0 : (previousK == k + 1 ? previousX : previousX + 1);
                while (x > snakeStartX && y > snakeStartX - k)
                {
                    x--;
                    y--;
                    matches.Add((x, y));
                }

                x = previousX;
                y = previousY;
            }

            matches.Reverse();
            return matches;
        }
    }
}
