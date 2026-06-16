namespace Retail.Api.Services;

/// <summary>
/// Releases stock held by carts that have sat past their expiry and tombstones them
/// (Story 2.3). Run on a timer by <c>CartExpirySweeper</c>, and callable directly (e.g. tests).
/// </summary>
public interface ICartSweepService
{
    /// <summary>
    /// Abandons every <c>Open</c> cart past its <c>ExpiresAt</c> (status → Abandoned) and
    /// releases the stock its active reservations were holding. Returns the number of carts swept.
    /// </summary>
    Task<int> SweepExpiredCartsAsync(CancellationToken ct);
}
