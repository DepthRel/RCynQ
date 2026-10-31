using RCynQ.MergeTools;

namespace RCynQ.UI.Services.Merging;

/// <summary>
/// The merge tools of the machine and the one the user chose to resolve conflicts with.
/// </summary>
public interface IMergeToolPreferences
{
    /// <summary>
    /// Finds the merge tools of the machine; the first search is kept for the next calls.
    /// </summary>
    /// <param name="rescan">Whether to search again, for a tool installed meanwhile.</param>
    /// <param name="cancellationToken">A token to stop the search.</param>
    /// <returns>The tools found.</returns>
    Task<IReadOnlyList<MergeTool>> GetToolsAsync(bool rescan = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the tool to resolve conflicts with: the one the user chose, otherwise the one Git uses, otherwise the first one found.
    /// </summary>
    /// <param name="cancellationToken">A token to stop the search.</param>
    /// <returns>The tool, or <see langword="null"/> when there is none.</returns>
    Task<MergeTool?> GetDefaultAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Remembers the tool the user chose.
    /// </summary>
    /// <param name="tool">The tool.</param>
    /// <exception cref="IOException">The choice could not be saved.</exception>
    /// <exception cref="UnauthorizedAccessException">The choice could not be saved.</exception>
    void SetDefault(MergeTool tool);

    /// <summary>
    /// Builds the tool the user describes: an executable and a template of its arguments.
    /// </summary>
    /// <param name="path">The executable.</param>
    /// <param name="arguments">The template of its arguments, as typed.</param>
    /// <returns>The tool, or <see langword="null"/> when the executable is missing or the template cannot be used.</returns>
    MergeTool? CreateCustom(string? path, string? arguments);
}
