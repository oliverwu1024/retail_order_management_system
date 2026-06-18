using Retail.Api.Common.Enums;

namespace Retail.Api.Ai;

/// <summary>
/// Sentiment of a piece of text: a score in −1..1 (= positive − negative confidence) plus the
/// overall <see cref="SentimentLabel"/>. Maps 1:1 onto a <c>Review</c>'s sentiment columns.
/// </summary>
public readonly record struct SentimentResult(decimal Score, SentimentLabel Label);
