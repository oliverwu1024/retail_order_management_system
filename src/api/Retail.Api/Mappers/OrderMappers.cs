using Retail.Api.Domain.Entities;
using Retail.Api.DTOs.Responses;

namespace Retail.Api.Mappers;

/// <summary>Explicit Order → DTO mapping (no AutoMapper — CODING_STANDARDS).</summary>
public static class OrderMappers
{
    public static OrderSummaryDto ToSummaryDto(this Order order) =>
        new(
            order.Id,
            order.OrderNumber,
            order.Status.ToString(),
            order.PlacedAt,
            order.TotalCents,
            order.Lines.Sum(line => line.Quantity));

    public static OrderDetailDto ToDetailDto(this Order order) =>
        new(
            order.Id,
            order.OrderNumber,
            order.Status.ToString(),
            order.PlacedAt,
            order.SubtotalCents,
            order.TaxCents,
            order.ShippingCents,
            order.TotalCents,
            order.ShippingAddress.ToDto(),
            order.BillingAddress.ToDto(),
            order.Lines
                .OrderBy(line => line.NameSnapshot)
                .Select(line => line.ToDto())
                .ToList());

    private static OrderAddressDto ToDto(this OrderAddressSnapshot a) =>
        new(a.RecipientName, a.Line1, a.Line2, a.City, a.Region, a.PostalCode, a.Country);

    private static OrderLineDto ToDto(this OrderLine line) =>
        new(line.NameSnapshot, line.SkuSnapshot, line.Quantity, line.UnitPriceCents, line.LineTotalCents);
}
