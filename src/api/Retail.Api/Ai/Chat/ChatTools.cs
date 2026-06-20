using System.Text.Json;
using Retail.Api.Ai.Contracts;

namespace Retail.Api.Ai.Chat;

/// <summary>
/// The tool catalogue offered to Claude in the support-chat loop (Phase 5A). Tool <b>names</b> are
/// the contract between the model and <c>ChatToolExecutor</c>; the JSON-Schema <c>input_schema</c>
/// constrains the arguments the model may emit.
/// </summary>
/// <remarks>
/// Three read-only tools, the confirmation-gated <c>start_return</c> (it only PROPOSES a refund — the
/// executor performs no mutation; an explicit user confirmation in the app runs the existing cancel
/// flow), and two Phase-7 stubs.
/// </remarks>
public static class ChatTools
{
    public const string GetOrder = "get_order";
    public const string ListMyRecentOrders = "list_my_recent_orders";
    public const string GetShippingStatus = "get_shipping_status";
    public const string StartReturn = "start_return";
    public const string GetMyLoyaltyBalance = "get_my_loyalty_balance";
    public const string ListMyVouchers = "list_my_vouchers";

    /// <summary>A schema taking a single required integer <c>orderNumber</c> (the human reference a customer quotes).</summary>
    private static readonly JsonElement OrderNumberSchema = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            orderNumber = new { type = "integer", description = "The customer's order number (e.g. 10012)." },
        },
        required = new[] { "orderNumber" },
    });

    /// <summary>A schema taking no arguments.</summary>
    private static readonly JsonElement NoArgsSchema = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new { },
    });

    /// <summary>A schema for proposing a return: a required integer <c>orderNumber</c> + an optional <c>reason</c>.</summary>
    private static readonly JsonElement StartReturnSchema = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            orderNumber = new { type = "integer", description = "The order number the customer wants to cancel/return." },
            reason = new { type = "string", description = "The customer's stated reason (optional)." },
        },
        required = new[] { "orderNumber" },
    });

    /// <summary>All tools offered to the model this phase.</summary>
    public static readonly IReadOnlyList<LlmTool> All = new[]
    {
        new LlmTool(ListMyRecentOrders,
            "List the signed-in customer's most recent orders (number, status, date, total). Use when they ask about their orders generally.",
            NoArgsSchema),
        new LlmTool(GetOrder,
            "Get the details of one of the signed-in customer's orders by its order number, including line items.",
            OrderNumberSchema),
        new LlmTool(GetShippingStatus,
            "Get the shipping/tracking status of one of the signed-in customer's orders by its order number.",
            OrderNumberSchema),
        new LlmTool(StartReturn,
            "Propose cancelling one of the signed-in customer's PAID (not-yet-shipped) orders for a full refund. This does NOT perform the refund — it returns an eligibility proposal the customer must confirm in the app. Use it when the customer asks to cancel, return, or get a refund on an order.",
            StartReturnSchema),
        new LlmTool(GetMyLoyaltyBalance,
            "Get the signed-in customer's loyalty point balance. (Not available yet.)",
            NoArgsSchema),
        new LlmTool(ListMyVouchers,
            "List the signed-in customer's vouchers. (Not available yet.)",
            NoArgsSchema),
    };
}
