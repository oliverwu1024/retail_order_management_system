namespace Retail.Api.Exceptions;

/// <summary>
/// Thrown when a stock operation can't be satisfied because there isn't enough available
/// inventory (<c>OnHand − Reserved &lt; requested</c>). Mapped to HTTP 409
/// <c>INVENTORY_INSUFFICIENT</c> by <c>ExceptionMiddleware</c>.
/// </summary>
public sealed class OutOfStockException : Exception
{
    public OutOfStockException(string message)
        : base(message)
    {
    }
}
