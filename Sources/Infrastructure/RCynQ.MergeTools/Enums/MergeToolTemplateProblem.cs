namespace RCynQ.MergeTools.Enums;

/// <summary>
/// What is wrong with a template of the arguments of a merge tool.
/// </summary>
public enum MergeToolTemplateProblem
{
    /// <summary>Nothing: the template can be used.</summary>
    None,

    /// <summary>The text is empty or a quotation mark is not closed.</summary>
    Unreadable,

    /// <summary>A placeholder in braces is not one RCynQ knows.</summary>
    UnknownPlaceholder,

    /// <summary>The local version is not handed to the tool.</summary>
    MissingLocal,

    /// <summary>The incoming version is not handed to the tool.</summary>
    MissingIncoming,

    /// <summary>The tool is not told where to write the result.</summary>
    MissingResult
}
