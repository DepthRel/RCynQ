using System.Globalization;
using RCynQ.Core.Api;
using RCynQ.Core.Enums;

namespace RCynQ.UI.ViewModels.Items;

/// <summary>
/// One line of the diff of a file: unchanged, only on the neighbour, or only in the working tree.
/// </summary>
public sealed class DiffLineRowViewModel : DiffRowViewModel
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DiffLineRowViewModel"/> class.
    /// </summary>
    /// <param name="line">The line.</param>
    public DiffLineRowViewModel(DiffLineDto line)
    {
        ArgumentNullException.ThrowIfNull(line);
        Kind = line.Kind;
        BaseNumber = line.BaseNumber?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        WorkNumber = line.WorkNumber?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        Text = line.Text;
    }

    /// <summary>Gets the role of the line.</summary>
    public DiffLineKind Kind { get; }

    /// <summary>Gets the number of the line in the version of the neighbour, or an empty text.</summary>
    public string BaseNumber { get; }

    /// <summary>Gets the number of the line in the working file, or an empty text.</summary>
    public string WorkNumber { get; }

    /// <summary>Gets the text of the line.</summary>
    public string Text { get; }

    /// <summary>Gets the sign before the line: <c>-</c> removed, <c>+</c> added, nothing unchanged.</summary>
    public string Marker => Kind switch
    {
        DiffLineKind.Removed => "-",
        DiffLineKind.Added => "+",
        _ => string.Empty
    };

    /// <summary>Gets a value indicating whether the line is only on the neighbour.</summary>
    public bool IsRemoved => Kind == DiffLineKind.Removed;

    /// <summary>Gets a value indicating whether the line is only in the working tree.</summary>
    public bool IsAdded => Kind == DiffLineKind.Added;
}
