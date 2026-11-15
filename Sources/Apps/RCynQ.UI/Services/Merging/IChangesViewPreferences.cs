using RCynQ.UI.Enums;

namespace RCynQ.UI.Services.Merging;

/// <summary>
/// Where the user wants the diff of a changed file shown and its changes chosen.
/// </summary>
public interface IChangesViewPreferences
{
    /// <summary>Gets the chosen place; <see cref="ChangesViewMode.BuiltIn"/> until the user chooses.</summary>
    ChangesViewMode Mode { get; }

    /// <summary>
    /// Remembers the chosen place.
    /// </summary>
    /// <param name="mode">The place.</param>
    /// <exception cref="IOException">The choice could not be saved.</exception>
    /// <exception cref="UnauthorizedAccessException">The choice could not be saved.</exception>
    void SetMode(ChangesViewMode mode);
}
