using System.Globalization;
using System.Text.Json;
using Retail.Api.Ai.Chat;
using Retail.Api.Ai.Contracts;
using Retail.Api.Common.Models;
using Retail.Api.Domain.Entities;
using Retail.Api.DTOs.Responses;
using Retail.Api.Repositories;

namespace Retail.Api.Services;

/// <summary>
/// Owner-scoped executor for the Phase-5A chat tools. Every lookup resolves the caller's
/// <c>CustomerProfileId</c> from the authenticated <c>appUserId</c> (never from tool input) and
/// filters by it, so a customer can only ever read their own orders — an order number that isn't
/// theirs comes back as "not found", never another user's data.
/// </summary>
public sealed class ChatToolExecutor : IChatToolExecutor
{
    private const int RecentOrderCount = 5;

    private readonly IOrderQueryService _orderQuery;
    private readonly IOrderRepository _orders;
    private readonly ICustomerProfileService _profiles;

    public ChatToolExecutor(IOrderQueryService orderQuery, IOrderRepository orders, ICustomerProfileService profiles)
    {
        _orderQuery = orderQuery;
        _orders = orders;
        _profiles = profiles;
    }

    /// <inheritdoc />
    public async Task<string> ExecuteAsync(string appUserId, LlmToolUse toolUse, CancellationToken ct)
    {
        switch (toolUse.Name)
        {
            case ChatTools.ListMyRecentOrders:
            {
                PagedResult<OrderSummaryDto> page = await _orderQuery.GetMyOrdersAsync(appUserId, 1, RecentOrderCount, ct);
                return Json(new
                {
                    orders = page.Items.Select(o => new
                    {
                        orderNumber = o.OrderNumber,
                        status = o.Status,
                        placedAt = o.PlacedAt.ToString("yyyy-MM-dd"),
                        totalCents = o.TotalCents,
                        itemCount = o.ItemCount,
                    }),
                });
            }

            case ChatTools.GetOrder:
            {
                if (!TryReadOrderNumber(toolUse.Input, out int number))
                {
                    return Json(new { error = "An order number is required." });
                }

                Order? order = await LoadOwnedOrderAsync(appUserId, number, ct);
                if (order is null)
                {
                    return Json(new { found = false, message = $"Order #{number} was not found in your account." });
                }

                return Json(new
                {
                    found = true,
                    order = new
                    {
                        orderNumber = order.OrderNumber,
                        status = order.Status.ToString(),
                        placedAt = order.PlacedAt.ToString("yyyy-MM-dd"),
                        totalCents = order.TotalCents,
                        lines = order.Lines.Select(l => new
                        {
                            name = l.NameSnapshot,
                            quantity = l.Quantity,
                            unitPriceCents = l.UnitPriceCents,
                        }),
                    },
                });
            }

            case ChatTools.GetShippingStatus:
            {
                if (!TryReadOrderNumber(toolUse.Input, out int number))
                {
                    return Json(new { error = "An order number is required." });
                }

                Order? order = await LoadOwnedOrderAsync(appUserId, number, ct);
                if (order is null)
                {
                    return Json(new { found = false, message = $"Order #{number} was not found in your account." });
                }

                Shipment? shipment = order.Shipment;
                return Json(new
                {
                    found = true,
                    orderNumber = order.OrderNumber,
                    orderStatus = order.Status.ToString(),
                    shipment = shipment is null
                        ? null
                        : (object)new
                        {
                            status = shipment.Status.ToString(),
                            carrier = shipment.Carrier,
                            trackingNumber = shipment.TrackingNumber,
                            shippedAt = shipment.ShippedAt?.ToString("yyyy-MM-dd"),
                            deliveredAt = shipment.DeliveredAt?.ToString("yyyy-MM-dd"),
                        },
                });
            }

            // Phase-7 features — stubbed so the model can answer honestly rather than guess.
            case ChatTools.GetMyLoyaltyBalance:
            case ChatTools.ListMyVouchers:
                return Json(new { available = false, message = "Loyalty points and vouchers aren't available yet — they're coming in a future release." });

            default:
                return Json(new { error = $"Unknown tool '{toolUse.Name}'." });
        }
    }

    private async Task<Order?> LoadOwnedOrderAsync(string appUserId, int orderNumber, CancellationToken ct)
    {
        Guid profileId = (await _profiles.GetMyProfileAsync(appUserId, ct)).Id;
        return await _orders.GetOwnedByOrderNumberAsync(orderNumber, profileId, ct);
    }

    private static bool TryReadOrderNumber(JsonElement input, out int orderNumber)
    {
        orderNumber = 0;
        if (input.ValueKind != JsonValueKind.Object || !input.TryGetProperty("orderNumber", out JsonElement value))
        {
            return false;
        }

        // The model usually emits an integer, but tolerate a numeric string and a whole-number float
        // (some models emit 10012.0 even when the schema says integer).
        return value.ValueKind switch
        {
            JsonValueKind.Number => TryReadWholeNumber(value, out orderNumber),
            JsonValueKind.String => TryParseWhole(value.GetString(), out orderNumber),
            _ => false,
        };
    }

    private static bool TryReadWholeNumber(JsonElement value, out int result)
    {
        if (value.TryGetInt32(out result))
        {
            return true;
        }
        result = 0;
        return value.TryGetDouble(out double d) && IsWholeInt(d, out result);
    }

    private static bool TryParseWhole(string? text, out int result)
    {
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out result))
        {
            return true;
        }
        result = 0;
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) && IsWholeInt(d, out result);
    }

    private static bool IsWholeInt(double d, out int result)
    {
        result = 0;
        if (d is >= int.MinValue and <= int.MaxValue && d == Math.Floor(d))
        {
            result = (int)d;
            return true;
        }

        return false;
    }

    private static string Json(object value) => JsonSerializer.Serialize(value);
}
