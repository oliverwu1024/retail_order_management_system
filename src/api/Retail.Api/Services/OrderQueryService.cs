using Retail.Api.Common.Models;
using Retail.Api.Domain.Entities;
using Retail.Api.DTOs.Responses;
using Retail.Api.Exceptions;
using Retail.Api.Mappers;
using Retail.Api.Repositories;

namespace Retail.Api.Services;

/// <summary>Read-side for customer orders (Story 2.4).</summary>
public sealed class OrderQueryService : IOrderQueryService
{
    private const int MaxPageSize = 100;

    private readonly IOrderRepository _orders;
    private readonly ICustomerProfileService _profiles; // resolves the caller's profile id

    public OrderQueryService(IOrderRepository orders, ICustomerProfileService profiles)
    {
        _orders = orders;
        _profiles = profiles;
    }

    /// <inheritdoc />
    public async Task<PagedResult<OrderSummaryDto>> GetMyOrdersAsync(string appUserId, int page, int pageSize, CancellationToken ct)
    {
        int safePage = page < 1 ? 1 : page;
        int safeSize = Math.Clamp(pageSize, 1, MaxPageSize);

        Guid profileId = (await _profiles.GetMyProfileAsync(appUserId, ct)).Id;
        (IReadOnlyList<Order> items, int total) = await _orders.GetPagedByProfileAsync(profileId, safePage, safeSize, ct);

        return new PagedResult<OrderSummaryDto>(
            items.Select(order => order.ToSummaryDto()).ToList(),
            total,
            safePage,
            safeSize);
    }

    /// <inheritdoc />
    public async Task<OrderDetailDto> GetMyOrderAsync(string appUserId, Guid orderId, CancellationToken ct)
    {
        Guid profileId = (await _profiles.GetMyProfileAsync(appUserId, ct)).Id;
        Order order = await _orders.GetOwnedByIdAsync(orderId, profileId, ct)
            ?? throw new NotFoundException($"Order '{orderId}' was not found.");
        return order.ToDetailDto();
    }

    /// <inheritdoc />
    public async Task<OrderDetailDto> GetOrderBySessionAsync(string stripeSessionId, CancellationToken ct)
    {
        Order order = await _orders.GetDetailByStripeSessionIdAsync(stripeSessionId, ct)
            ?? throw new NotFoundException("No order was found for that checkout session.");
        return order.ToDetailDto();
    }
}
