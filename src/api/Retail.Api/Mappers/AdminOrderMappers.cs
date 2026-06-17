using Retail.Api.Domain.Entities;
using Retail.Api.DTOs.Responses;

namespace Retail.Api.Mappers;

/// <summary>
/// Explicit Order → admin DTO mapping (no AutoMapper — CODING_STANDARDS). Surfaces the customer
/// identity, payment ledger, and shipment that the customer-facing <see cref="OrderMappers"/> omits.
/// </summary>
public static class AdminOrderMappers
{
    public static AdminOrderSummaryDto ToAdminSummaryDto(this Order order) =>
        new(
            order.Id,
            order.OrderNumber,
            order.Status.ToString(),
            order.PlacedAt,
            order.TotalCents,
            order.Lines.Sum(line => line.Quantity),
            CustomerEmailOf(order),
            order.Shipment?.Status.ToString());

    public static AdminOrderDetailDto ToAdminDetailDto(this Order order) =>
        new(
            order.Id,
            order.OrderNumber,
            order.Status.ToString(),
            order.PlacedAt,
            CustomerEmailOf(order),
            order.SubtotalCents,
            order.TaxCents,
            order.ShippingCents,
            order.TotalCents,
            order.ShippingAddress.ToDto(),
            order.BillingAddress.ToDto(),
            order.Lines.OrderBy(line => line.NameSnapshot).Select(line => line.ToDto()).ToList(),
            order.Payments.OrderBy(payment => payment.CreatedAt).Select(payment => payment.ToDto()).ToList(),
            order.Shipment?.ToDto());

    // Member email lives on the Identity user behind the profile; a guest order carries it directly.
    private static string CustomerEmailOf(Order order) =>
        order.GuestEmail ?? order.CustomerProfile?.User?.Email ?? "(unknown)";

    private static PaymentDto ToDto(this Payment payment) =>
        new(
            payment.Provider,
            payment.AmountCents,
            payment.Currency,
            payment.Status.ToString(),
            payment.StripePaymentIntentId,
            payment.CreatedAt);

    private static ShipmentDto ToDto(this Shipment shipment) =>
        new(shipment.Carrier, shipment.TrackingNumber, shipment.Status.ToString(), shipment.ShippedAt, shipment.DeliveredAt);

    private static OrderAddressDto ToDto(this OrderAddressSnapshot a) =>
        new(a.RecipientName, a.Line1, a.Line2, a.City, a.Region, a.PostalCode, a.Country);

    private static OrderLineDto ToDto(this OrderLine line) =>
        new(line.NameSnapshot, line.SkuSnapshot, line.Quantity, line.UnitPriceCents, line.LineTotalCents);
}
