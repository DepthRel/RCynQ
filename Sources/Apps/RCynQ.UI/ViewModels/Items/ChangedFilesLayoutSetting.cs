using RCynQ.UI.Services.Configuration;

namespace RCynQ.UI.ViewModels.Items;

/// <summary>
/// Keeps how lists of changed files are laid out, one choice for every such list of the application.
/// </summary>
public static class ChangedFilesLayoutSetting
{
    /// <summary>The name of the interface setting.</summary>
    public const string KEY = "syncFilesView";

    /// <summary>The value of the setting for the tree of folders.</summary>
    private const string TREE_VALUE = "tree";

    /// <summary>The value of the setting for the flat list of paths.</summary>
    private const string FLAT_VALUE = "flat";

    /// <summary>
    /// Reads the choice.
    /// </summary>
    /// <param name="store">The store of the interface settings, or <see langword="null"/> when the choice is not kept.</param>
    /// <returns><see langword="true"/> for the tree of folders.</returns>
    public static bool IsTree(IUiSettingsStore? store) => string.Equals(store?.Get(KEY), TREE_VALUE, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Keeps the choice for the next start.
    /// </summary>
    /// <param name="store">The store of the interface settings, or <see langword="null"/> when the choice is not kept.</param>
    /// <param name="isTree">Whether the tree of folders is chosen.</param>
    public static void Save(IUiSettingsStore? store, bool isTree)
    {
        try
        {
            store?.Set(KEY, isTree ? TREE_VALUE : FLAT_VALUE);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Only the choice for the next start is lost; the list is already laid out.
        }
    }
}
