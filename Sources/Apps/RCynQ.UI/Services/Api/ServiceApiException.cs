using System.Net;

namespace RCynQ.UI.Services.Api;

/// <summary>
/// Thrown when the local RCynQ service answers with an error.
/// </summary>
public sealed class ServiceApiException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ServiceApiException"/> class.
    /// </summary>
    /// <param name="statusCode">The HTTP status code.</param>
    /// <param name="message">The error message reported by the service.</param>
    public ServiceApiException(HttpStatusCode statusCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
    }

    /// <summary>
    /// Gets the HTTP status code.
    /// </summary>
    public HttpStatusCode StatusCode { get; }
}
