using RCynQ.MergeTools;

namespace RCynQ.UI.ViewModels.Items;

/// <summary>
/// One entry of the list of merge tools in the settings: a found tool, or the tool the user describes.
/// </summary>
/// <param name="Name">The name shown in the list.</param>
/// <param name="Tool">The found tool, or <see langword="null"/> for the tool the user describes.</param>
public sealed record MergeToolOptionViewModel(string Name, MergeTool? Tool)
{
    /// <summary>
    /// Gets a value indicating whether this is the entry of the tool the user describes.
    /// </summary>
    public bool IsCustom => Tool is null;
}
