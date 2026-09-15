namespace RCynQ.Core.Exceptions;

/// <summary>
/// Base exception for all SyncBridge domain errors.
/// </summary>
/// <remarks>
/// Specific infrastructure and operation failures derive from this type so
/// callers can catch a single, stable contract.
/// </remarks>
public class SyncBridgeException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SyncBridgeException"/> class.
    /// </summary>
    public SyncBridgeException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SyncBridgeException"/> class
    /// with a specified error message.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    public SyncBridgeException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SyncBridgeException"/> class
    /// with a specified error message and a reference to the inner exception.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    /// <param name="innerException">
    /// The exception that is the cause of the current exception, or <see langword="null"/>.
    /// </param>
    public SyncBridgeException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
