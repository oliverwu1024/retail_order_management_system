using Retail.Api.DTOs.Requests;
using Retail.Api.DTOs.Responses;

namespace Retail.Api.Services;

/// <summary>Customer product reviews (Phase 4, Story 4.1): submit (purchase-verified) + public listing.</summary>
public interface IReviewService
{
    /// <summary>
    /// Records a review by the current customer for a product. Enforces: product exists (404),
    /// the customer actually purchased it (422), and one review per customer per product (409).
    /// </summary>
    Task<ReviewDto> SubmitReviewAsync(string appUserId, Guid productId, SubmitReviewRequest request, CancellationToken ct);

    /// <summary>A public, paged page of a product's reviews plus the whole-product aggregate.</summary>
    Task<ReviewListDto> ListReviewsAsync(Guid productId, int page, int pageSize, CancellationToken ct);
}
