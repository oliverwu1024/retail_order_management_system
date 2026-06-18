using Retail.Api.Common.Models;

namespace Retail.Api.DTOs.Responses;

/// <summary>
/// Payload for <c>GET /api/v1/products/{id}/reviews</c>: one page of reviews plus the
/// whole-product aggregate the storefront needs to render the average + distribution chart.
/// </summary>
public sealed record ReviewListDto(
    PagedResult<ReviewDto> Page,
    ReviewSummaryDto Summary);
