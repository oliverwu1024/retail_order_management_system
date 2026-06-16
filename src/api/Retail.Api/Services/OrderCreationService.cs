using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Retail.Api.Common.Abstractions;
using Retail.Api.Common.Enums;
using Retail.Api.Data;
using Retail.Api.Domain.Entities;
using Retail.Api.Exceptions;
using Retail.Api.Repositories;

namespace Retail.Api.Services;

/// <summary>
/// Turns a paid Stripe checkout into an order (Story 2.2) — the "commit" half of the
/// reservation lifecycle.
/// </summary>
/// <remarks>
/// <para>
/// IDEMPOTENT: Stripe delivers webhooks at-least-once, so the first thing we do is check whether
/// an order already exists for this session id and, if so, return it untouched. (The webhook
/// also dedups on the event id; this is the second line of defence.)
/// </para>
/// <para>
/// ATOMIC: order assembly + the stock commit + the cart conversion all run in ONE transaction.
/// We compute the totals from the cart's price snapshots (not from Stripe's amount), so the
/// stored order is internally consistent regardless of any rounding differences. The reservation
/// holds taken at checkout-start are turned into real <c>OnHand</c> decrements here — the units
/// were already ours, so no fresh concurrency check is needed.
/// </para>
/// </remarks>
public sealed class OrderCreationService : IOrderCreationService
{
    private const double GstRate = 0.10; // flat 10% GST (MVP)
    private const int ShippingCents = 0; // free shipping (MVP)

    private readonly RetailDbContext _db; // for the transaction (CODING_STANDARDS-sanctioned)
    private readonly IOrderRepository _orders;
    private readonly ICartRepository _carts;
    private readonly IInventoryReservationRepository _reservations;
    private readonly TimeProvider _timeProvider;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly ILogger<OrderCreationService> _logger;

    public OrderCreationService(
        RetailDbContext db,
        IOrderRepository orders,
        ICartRepository carts,
        IInventoryReservationRepository reservations,
        TimeProvider timeProvider,
        ICurrentUserAccessor currentUser,
        ILogger<OrderCreationService> logger)
    {
        _db = db;
        _orders = orders;
        _carts = carts;
        _reservations = reservations;
        _timeProvider = timeProvider;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Order> CreateOrderFromCheckoutAsync(CheckoutCompletion completion, CancellationToken ct)
    {
        // Idempotency: a redelivered webhook for an already-finalised session returns the order.
        Order? alreadyCreated = await _orders.GetByStripeSessionIdAsync(completion.StripeSessionId, ct);
        if (alreadyCreated is not null)
        {
            return alreadyCreated;
        }

        // Identity invariant: an order is a member (CustomerProfileId) XOR a guest (email).
        // Stripe always collects an email in payment mode, but never trust the input blindly.
        if (completion.CustomerProfileId is null && string.IsNullOrWhiteSpace(completion.CustomerEmail))
        {
            throw new ConflictException("Checkout completion has neither a customer profile nor a guest email.");
        }

        Cart cart = await _carts.GetOpenCartByIdAsync(completion.CartId, ct)
            ?? throw new NotFoundException($"Cart '{completion.CartId}' is no longer open for checkout.");
        if (cart.Items.Count == 0)
        {
            throw new ConflictException($"Cart '{completion.CartId}' has no items to order.");
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        string? actor = _currentUser.UserId; // null in the webhook (no user principal) — fine

        // Money math in long to avoid int overflow on a high-value cart, then bounded into int.
        long subtotalLong = cart.Items.Sum(item => (long)item.UnitPriceCentsSnapshot * item.Quantity);
        long taxLong = (long)Math.Round(subtotalLong * GstRate, MidpointRounding.AwayFromZero);
        long totalLong = subtotalLong + taxLong + ShippingCents;
        if (totalLong > int.MaxValue)
        {
            throw new ConflictException("Order total exceeds the maximum supported amount.");
        }

        int subtotalCents = (int)subtotalLong;
        int taxCents = (int)taxLong;
        int totalCents = (int)totalLong;

        Order order = BuildOrder(completion, cart, subtotalCents, taxCents, totalCents, now);

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        // Persist the order graph first so order.Id (+ the DB-assigned OrderNumber) exists for
        // the reservation links below. The UNIQUE index on Payment.StripeSessionId is the
        // idempotency backstop: if a concurrent webhook redelivery already created the order,
        // this duplicate Payment insert violates the index — we roll back and return the winner
        // rather than creating a second order. (SQL Server raises error 2601/2627.)
        _orders.AddOrder(order);
        try
        {
            await _orders.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            await tx.RollbackAsync(ct);
            return await _orders.GetByStripeSessionIdAsync(completion.StripeSessionId, ct)
                ?? throw new ConflictException("Order creation conflicted; please retry.");
        }

        // Commit the holds: each active reservation becomes a real OnHand decrement, re-homed
        // from the cart onto the order. The units were already reserved for us — no race here.
        IReadOnlyList<InventoryReservation> active = await _reservations.GetActiveCartReservationsAsync(completion.CartId, ct);
        if (active.Count == 0)
        {
            // The holds were released (e.g. the cart was swept after a long webhook delay).
            // Fail LOUDLY (the transaction rolls back, no order is created) rather than silently
            // create an order with no stock movement. Stripe retries; a Phase-8 reconciliation
            // job re-acquires stock or refunds. See docs/PHASE_2_SCOPE.md "checkout hardening".
            throw new ConflictException(
                $"Reservations for cart '{completion.CartId}' are no longer held; the order cannot be finalised.");
        }

        foreach (InventoryReservation reservation in active)
        {
            await _reservations.CommitReservedAsync(reservation.InventoryItemId, reservation.Quantity, now, actor, ct);
            reservation.Status = ReservationStatus.Committed;
            reservation.OrderId = order.Id;
            reservation.CartId = null; // now owned by the order, not the cart
        }

        cart.Status = CartStatus.Converted; // tombstone the cart

        await _db.SaveChangesAsync(ct); // reservation status + cart status
        await tx.CommitAsync(ct);

        _logger.LogInformation(
            "Order {OrderId} (#{OrderNumber}) created from cart {CartId}; {LineCount} line(s).",
            order.Id, order.OrderNumber, completion.CartId, order.Lines.Count);
        return order;
    }

    // Assembles the full order graph (order + lines + breakdown + payment) in memory.
    private static Order BuildOrder(
        CheckoutCompletion completion, Cart cart, int subtotalCents, int taxCents, int totalCents, DateTimeOffset now)
    {
        var order = new Order
        {
            // Member XOR guest: a profile id wins; otherwise the guest email carries the identity.
            CustomerProfileId = completion.CustomerProfileId,
            GuestEmail = completion.CustomerProfileId is null ? completion.CustomerEmail : null,
            Status = OrderStatus.Paid,
            SubtotalCents = subtotalCents,
            TaxCents = taxCents,
            ShippingCents = ShippingCents,
            TotalCents = totalCents,
            ShippingAddress = completion.ShippingAddress,
            BillingAddress = completion.BillingAddress,
            PlacedAt = now,
            // OrderNumber is assigned by the Seq_OrderNumber sequence on insert.
        };

        foreach (CartItem item in cart.Items)
        {
            ProductVariant? variant = item.ProductVariant;
            order.Lines.Add(new OrderLine
            {
                ProductVariantId = item.ProductVariantId,
                Quantity = item.Quantity,
                UnitPriceCents = item.UnitPriceCentsSnapshot,
                LineTotalCents = (int)((long)item.UnitPriceCentsSnapshot * item.Quantity),
                SkuSnapshot = variant?.Sku ?? string.Empty,
                NameSnapshot = variant?.Product?.Name ?? string.Empty,
            });
        }

        order.PriceBreakdown = new OrderPriceBreakdown
        {
            SubtotalCents = subtotalCents,
            ShippingCents = ShippingCents,
            TaxCents = taxCents,
            TotalCents = totalCents,
            // voucher/loyalty fields stay 0 until Phase 7
        };

        order.Payments.Add(new Payment
        {
            Provider = "stripe",
            StripeSessionId = completion.StripeSessionId,
            StripePaymentIntentId = completion.PaymentIntentId,
            AmountCents = totalCents,
            Currency = "AUD",
            Status = PaymentStatus.Succeeded,
        });

        return order;
    }
}
