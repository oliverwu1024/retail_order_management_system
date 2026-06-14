namespace Retail.Api.Exceptions;

/// <summary>
/// Thrown when a request conflicts with current state — e.g. a duplicate SKU or
/// slug, or a business-rule violation. Mapped to HTTP 409 by <c>ExceptionMiddleware</c>.
/// </summary>
public sealed class ConflictException : Exception
{
    public ConflictException(string message)
        : base(message)
    {
    }
}
