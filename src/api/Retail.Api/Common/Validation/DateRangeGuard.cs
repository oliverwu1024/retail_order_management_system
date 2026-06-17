using Retail.Api.Common.Models;

namespace Retail.Api.Common.Validation;

/// <summary>
/// Cross-field validation for optional <c>[from, to]</c> query-string date ranges shared by the
/// audit viewer (§7), the order workbench (§8), and the sales report (§9). A reversed range
/// (<c>from &gt; to</c>) yields an always-false predicate that reads as "no data" with a 200 — a
/// silent client error — so callers reject it up front; the report additionally caps the span to
/// bound the unindexed in-memory aggregation.
/// </summary>
public static class DateRangeGuard
{
    /// <summary>
    /// Returns a 422 <see cref="ApiResponse"/> if the range is reversed (or, when
    /// <paramref name="maxSpanDays"/> &gt; 0, wider than the cap), otherwise <c>null</c>.
    /// Both bounds must be present for either check to apply (each is independently optional).
    /// </summary>
    public static ApiResponse? Validate(DateTimeOffset? from, DateTimeOffset? to, int maxSpanDays = 0)
    {
        if (from.HasValue && to.HasValue)
        {
            if (from.Value > to.Value)
            {
                return Fail("'from' must be on or before 'to'.", "from");
            }

            if (maxSpanDays > 0 && (to.Value - from.Value).TotalDays > maxSpanDays)
            {
                return Fail($"Date range cannot exceed {maxSpanDays} days.", "to");
            }
        }

        return null;
    }

    private static ApiResponse Fail(string message, string field) =>
        ApiResponse.Fail(
            "Validation failed.",
            new[] { new ApiError { Code = "VALIDATION_ERROR", Message = message, Field = field } });
}
