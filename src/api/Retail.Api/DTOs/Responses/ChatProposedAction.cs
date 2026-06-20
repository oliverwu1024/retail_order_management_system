namespace Retail.Api.DTOs.Responses;

/// <summary>
/// A state-changing action the assistant has PROPOSED but not performed (Phase 5A, Chunk 3). The
/// storefront renders a confirmation card; only an explicit user confirmation executes it — an LLM
/// never moves money on its own.
/// </summary>
/// <param name="Type">Discriminator for the FE card. Currently only <c>"confirm_return"</c>.</param>
/// <param name="OrderId">The order's surrogate id — what the confirm call (the existing cancel endpoint) keys on.</param>
/// <param name="OrderNumber">The human order number, for display.</param>
/// <param name="RefundAmountCents">The refund the customer would receive (the order total), for display.</param>
public sealed record ChatProposedAction(string Type, Guid OrderId, int OrderNumber, int RefundAmountCents);
