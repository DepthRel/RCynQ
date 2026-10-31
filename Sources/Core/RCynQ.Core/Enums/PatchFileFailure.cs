namespace RCynQ.Core.Enums;

/// <summary>
/// Why a patch file could not be applied or removed.
/// </summary>
public enum PatchFileFailure
{
    /// <summary>Git refused it for a reason the program does not know; its output tells more.</summary>
    Other,

    /// <summary>The file holds no patch.</summary>
    NotAPatch,

    /// <summary>The patch names a path outside the working tree or inside the repository itself.</summary>
    UnsafePath,

    /// <summary>The patch does not fit the current content of a file.</summary>
    DoesNotApply,

    /// <summary>The patch creates a file that exists already.</summary>
    AlreadyExists,

    /// <summary>The patch changes a file the working tree does not have.</summary>
    MissingFile,

    /// <summary>The choice of files is too long for a command line.</summary>
    TooManyFiles
}
