namespace Retail.Api.Exceptions;

/// <summary>
/// Thrown when an upstream external dependency (the AI provider, etc.) is unavailable or returns an
/// error after resilience retries. Mapped to HTTP 503 <c>EXTERNAL_SERVICE_UNAVAILABLE</c> by
/// <c>ExceptionMiddleware</c> — distinct from a client error: the request was valid, the dependency
/// failed.
/// </summary>
public sealed class ExternalServiceException : Exception
{
    public ExternalServiceException(string message)
        : base(message)
    {
    }

    public ExternalServiceException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
