namespace Retail.Api.DTOs.Responses;

/// <summary>
/// One product review as shown on the storefront (Phase 4). <paramref name="CustomerName"/>
/// is the author's display name (never their email — PII stays off the wire).
/// <paramref name="SentimentScore"/> / <paramref name="SentimentLabel"/> are <c>null</c> until the
/// background scorer (Chunk 3) has run.
/// </summary>
public sealed record ReviewDto(
    Guid Id,
    string CustomerName,
    int Rating,
    string Body,
    decimal? SentimentScore,
    string? SentimentLabel,
    DateTimeOffset CreatedAt);
