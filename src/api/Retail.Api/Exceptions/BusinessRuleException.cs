namespace Retail.Api.Exceptions;

/// <summary>
/// Thrown when a request is well-formed and authorized but violates a business
/// precondition that can only be checked against state (e.g. reviewing a product
/// you never purchased). Mapped to HTTP 422 Unprocessable Entity by
/// <c>ExceptionMiddleware</c> — distinct from <see cref="ConflictException"/> (409,
/// a state collision) and from request-shape validation (also 422, but caught at
/// the controller via FluentValidation before the service runs).
/// </summary>
public sealed class BusinessRuleException : Exception
{
    public BusinessRuleException(string message)
        : base(message)
    {
    }
}
