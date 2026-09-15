namespace RCynQ.Core.Exceptions;

/// <summary>
/// Thrown when applying a patch fails.
/// </summary>
public sealed class PatchApplyException : SyncBridgeException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PatchApplyException"/> class.
    /// </summary>
    public PatchApplyException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PatchApplyException"/> class
    /// with a specified error message.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    public PatchApplyException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PatchApplyException"/> class
    /// with a specified error message and a reference to the inner exception.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    /// <param name="innerException">
    /// The exception that is the cause of the current exception, or <see langword="null"/>.
    /// </param>
    public PatchApplyException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
