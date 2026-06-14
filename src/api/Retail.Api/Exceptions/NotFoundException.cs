namespace Retail.Api.Exceptions;

/// <summary>
/// Thrown when a requested resource does not exist (or is filtered out, e.g.
/// soft-deleted). Mapped to HTTP 404 by <c>ExceptionMiddleware</c>.
/// </summary>
public sealed class NotFoundException : Exception
{
    public NotFoundException(string message)
        : base(message)
    {
    }
}
