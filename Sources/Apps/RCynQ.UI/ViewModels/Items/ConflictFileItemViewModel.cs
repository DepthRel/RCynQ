using RCynQ.Core.Patches;
using RCynQ.Localization;

namespace RCynQ.UI.ViewModels.Items;

/// <summary>
/// A row of the list of files in the conflict dialog: the path and, in the language of the user, what is wrong with it.
/// </summary>
/// <remarks>
/// The service stores the reason as the key of a text and its values, so it can be worded for whoever reads it. The
/// row words it; shown as it is stored, the reason is a line of separators, a key and a path.
/// </remarks>
public sealed class ConflictFileItemViewModel
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ConflictFileItemViewModel"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the texts.</param>
    /// <param name="file">The file in conflict.</param>
    public ConflictFileItemViewModel(ILocalizer localizer, ConflictFile file)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(file);
        Path = file.Path;
        Detail = LocalizedText.Render(file.Detail, localizer);
    }

    /// <summary>
    /// Gets the path of the file, relative to the working tree.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Gets what is wrong with the file, or <see langword="null"/> when nothing is known.
    /// </summary>
    public string? Detail { get; }

    /// <summary>
    /// Gets a value indicating whether there is a reason to show.
    /// </summary>
    public bool HasDetail => !string.IsNullOrWhiteSpace(Detail);
}
