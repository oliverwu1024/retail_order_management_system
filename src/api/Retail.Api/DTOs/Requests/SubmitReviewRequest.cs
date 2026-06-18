namespace Retail.Api.DTOs.Requests;

/// <summary>
/// Body for <c>POST /api/v1/products/{id}/reviews</c> (Phase 4, Story 4.1). The product
/// is taken from the route; the author is taken from the auth cookie — never the body.
/// </summary>
public sealed record SubmitReviewRequest
{
    /// <summary>Star rating 1–5 (validated by <c>SubmitReviewRequestValidator</c>; DB CHECK is the backstop).</summary>
    public int Rating { get; init; }

    /// <summary>Review text (1–4000 chars).</summary>
    public string Body { get; init; } = string.Empty;
}
