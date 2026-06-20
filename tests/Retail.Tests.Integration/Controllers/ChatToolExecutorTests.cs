using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Retail.Api.Ai.Contracts;
using Retail.Api.Common.Enums;
using Retail.Api.Data;
using Retail.Api.Domain.Entities;
using Retail.Api.Services;

namespace Retail.Tests.Integration.Controllers;

/// <summary>
/// Owner-scoping of the Phase-5A chat tools, exercised directly against the DI container + real DB.
/// The security property under test: a customer's tool call only ever returns THEIR data — an order
/// number belonging to someone else comes back as "not found", never another user's order.
/// </summary>
[Collection("api")]
public class ChatToolExecutorTests
{
    private const int PriceCents = 4200;
    private readonly ApiFactory _factory;

    public ChatToolExecutorTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetOrder_OwnedByCaller_ReturnsTheOrder()
    {
        (string appUserId, Guid profileId) = await RegisterCustomerAsync("Owner");
        int orderNumber = await SeedOrderAsync(profileId, withShipment: false);

        JsonElement result = await ExecuteAsync(appUserId, "get_order", new { orderNumber });

        Assert.True(result.GetProperty("found").GetBoolean());
        Assert.Equal(orderNumber, result.GetProperty("order").GetProperty("orderNumber").GetInt32());
    }

    [Fact]
    public async Task GetOrder_OwnedByAnotherCustomer_ReturnsNotFound()
    {
        (string callerUserId, _) = await RegisterCustomerAsync("Caller");
        (_, Guid otherProfileId) = await RegisterCustomerAsync("Other");
        int othersOrderNumber = await SeedOrderAsync(otherProfileId, withShipment: false);

        // The caller asks for an order that exists, but isn't theirs.
        JsonElement result = await ExecuteAsync(callerUserId, "get_order", new { orderNumber = othersOrderNumber });

        Assert.False(result.GetProperty("found").GetBoolean()); // not-owned ≡ not-found: no leak
    }

    [Fact]
    public async Task GetShippingStatus_ShippedOrder_ReturnsTracking()
    {
        (string appUserId, Guid profileId) = await RegisterCustomerAsync("Shipped");
        int orderNumber = await SeedOrderAsync(profileId, withShipment: true);

        JsonElement result = await ExecuteAsync(appUserId, "get_shipping_status", new { orderNumber });

        Assert.True(result.GetProperty("found").GetBoolean());
        JsonElement shipment = result.GetProperty("shipment");
        Assert.Equal(JsonValueKind.Object, shipment.ValueKind);
        Assert.Equal("Shipped", shipment.GetProperty("status").GetString());
        Assert.Equal("AusPost", shipment.GetProperty("carrier").GetString());
    }

    [Fact]
    public async Task GetShippingStatus_NotShipped_ReturnsNullShipment()
    {
        (string appUserId, Guid profileId) = await RegisterCustomerAsync("Pending");
        int orderNumber = await SeedOrderAsync(profileId, withShipment: false);

        JsonElement result = await ExecuteAsync(appUserId, "get_shipping_status", new { orderNumber });

        Assert.True(result.GetProperty("found").GetBoolean());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("shipment").ValueKind);
    }

    [Fact]
    public async Task ListMyRecentOrders_ReturnsOnlyCallersOrders()
    {
        (string appUserId, Guid profileId) = await RegisterCustomerAsync("Lister");
        int orderNumber = await SeedOrderAsync(profileId, withShipment: false);

        JsonElement result = await ExecuteAsync(appUserId, "list_my_recent_orders", new { });

        JsonElement orders = result.GetProperty("orders");
        Assert.Equal(1, orders.GetArrayLength());
        Assert.Equal(orderNumber, orders[0].GetProperty("orderNumber").GetInt32());
    }

    [Fact]
    public async Task LoyaltyAndVoucherTools_ReportNotAvailable()
    {
        (string appUserId, _) = await RegisterCustomerAsync("Phase7");

        JsonElement loyalty = await ExecuteAsync(appUserId, "get_my_loyalty_balance", new { });
        JsonElement vouchers = await ExecuteAsync(appUserId, "list_my_vouchers", new { });

        Assert.False(loyalty.GetProperty("available").GetBoolean());
        Assert.False(vouchers.GetProperty("available").GetBoolean());
    }

    [Fact]
    public async Task StartReturn_OwnedPaidOrder_ProposesRefundWithoutMutating()
    {
        (string appUserId, Guid profileId) = await RegisterCustomerAsync("Returner");
        int orderNumber = await SeedOrderAsync(profileId, withShipment: false); // Paid

        ChatToolResult result = await ExecuteRawAsync(appUserId, "start_return", new { orderNumber, reason = "changed mind" });

        JsonElement json = JsonSerializer.Deserialize<JsonElement>(result.Content);
        Assert.True(json.GetProperty("eligible").GetBoolean());
        Assert.NotNull(result.ProposedAction);
        Assert.Equal("confirm_return", result.ProposedAction!.Type);
        Assert.Equal(orderNumber, result.ProposedAction.OrderNumber);
        Assert.Equal(PriceCents, result.ProposedAction.RefundAmountCents); // refund = the order total

        // Proposal only — the order is untouched (still Paid). The refund happens on confirm.
        using IServiceScope scope = _factory.Services.CreateScope();
        RetailDbContext db = scope.ServiceProvider.GetRequiredService<RetailDbContext>();
        OrderStatus status = await db.Orders.Where(o => o.OrderNumber == orderNumber).Select(o => o.Status).SingleAsync();
        Assert.Equal(OrderStatus.Paid, status);
    }

    [Fact]
    public async Task StartReturn_FulfilledOrder_IsIneligibleWithNoProposal()
    {
        (string appUserId, Guid profileId) = await RegisterCustomerAsync("Shipped");
        int orderNumber = await SeedOrderAsync(profileId, withShipment: true); // Fulfilled

        ChatToolResult result = await ExecuteRawAsync(appUserId, "start_return", new { orderNumber });

        Assert.False(JsonSerializer.Deserialize<JsonElement>(result.Content).GetProperty("eligible").GetBoolean());
        Assert.Null(result.ProposedAction);
    }

    [Fact]
    public async Task StartReturn_AnotherCustomersOrder_ReturnsNotFound()
    {
        (string callerUserId, _) = await RegisterCustomerAsync("Caller");
        (_, Guid otherProfileId) = await RegisterCustomerAsync("Other");
        int othersOrder = await SeedOrderAsync(otherProfileId, withShipment: false);

        ChatToolResult result = await ExecuteRawAsync(callerUserId, "start_return", new { orderNumber = othersOrder });

        Assert.False(JsonSerializer.Deserialize<JsonElement>(result.Content).GetProperty("found").GetBoolean());
        Assert.Null(result.ProposedAction); // not-owned ≡ not-found: no proposal leaks
    }

    // ── helpers ───────────────────────────────────────────────────────────────────

    private async Task<JsonElement> ExecuteAsync(string appUserId, string toolName, object args)
    {
        ChatToolResult result = await ExecuteRawAsync(appUserId, toolName, args);
        return JsonSerializer.Deserialize<JsonElement>(result.Content);
    }

    private async Task<ChatToolResult> ExecuteRawAsync(string appUserId, string toolName, object args)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        IChatToolExecutor executor = scope.ServiceProvider.GetRequiredService<IChatToolExecutor>();
        var toolUse = new LlmToolUse(Id: "t1", Name: toolName, Input: JsonSerializer.SerializeToElement(args));
        return await executor.ExecuteAsync(appUserId, toolUse, CancellationToken.None);
    }

    /// <summary>Registers a Customer, returns the Identity user id (appUserId) + the CustomerProfileId.</summary>
    private async Task<(string AppUserId, Guid ProfileId)> RegisterCustomerAsync(string displayName)
    {
        HttpClient client = _factory.CreateClient();
        string email = $"cust-{Guid.NewGuid():N}@test.local";
        string csrf = ExtractCookie(await client.GetAsync("/api/v1/auth/csrf"), "csrf");
        HttpResponseMessage register = await PostJsonAsync(client, "/api/v1/auth/register",
            new { email, password = "Sup3rSecret!pw", displayName }, csrf);
        register.EnsureSuccessStatusCode();

        // GET /profile lazily materialises the CustomerProfile and returns its id.
        JsonElement profile = (await (await client.GetAsync("/api/v1/profile")).Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data");
        Guid profileId = Guid.Parse(profile.GetProperty("id").GetString()!);

        using IServiceScope scope = _factory.Services.CreateScope();
        RetailDbContext db = scope.ServiceProvider.GetRequiredService<RetailDbContext>();
        string appUserId = await db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync();
        return (appUserId, profileId);
    }

    /// <summary>Seeds a product + a Paid order (one line) owned by the profile, optionally shipped. Returns its OrderNumber.</summary>
    private async Task<int> SeedOrderAsync(Guid profileId, bool withShipment)
    {
        string suffix = Guid.NewGuid().ToString("N")[..8];
        using IServiceScope scope = _factory.Services.CreateScope();
        RetailDbContext db = scope.ServiceProvider.GetRequiredService<RetailDbContext>();

        var category = new Category { Name = $"Cat {suffix}", Slug = $"cat-{suffix}" };
        var product = new Product
        {
            Category = category,
            Sku = $"SKU-{suffix}",
            Slug = $"product-{suffix}",
            Name = $"Product {suffix}",
            IsPublished = true,
        };
        var variant = new ProductVariant
        {
            Product = product,
            Sku = $"VAR-{suffix}",
            Options = new Dictionary<string, string> { ["size"] = "M" },
            PriceCents = PriceCents,
            IsActive = true,
        };
        db.AddRange(category, product, variant, new InventoryItem { Variant = variant, OnHand = 5 });
        await db.SaveChangesAsync();

        var address = new OrderAddressSnapshot { Line1 = "1 Test St", City = "Sydney", PostalCode = "2000", Country = "AU" };
        var order = new Order
        {
            CustomerProfileId = profileId,
            Status = withShipment ? OrderStatus.Fulfilled : OrderStatus.Paid,
            SubtotalCents = PriceCents,
            TotalCents = PriceCents,
            ShippingAddress = address,
            BillingAddress = address,
            PlacedAt = _factory.Services.GetRequiredService<TimeProvider>().GetUtcNow(),
        };
        order.Lines.Add(new OrderLine
        {
            ProductVariantId = variant.Id,
            Quantity = 1,
            UnitPriceCents = PriceCents,
            LineTotalCents = PriceCents,
            SkuSnapshot = variant.Sku,
            NameSnapshot = product.Name,
        });
        if (withShipment)
        {
            order.Shipment = new Shipment
            {
                Carrier = "AusPost",
                TrackingNumber = "AP123456789",
                Status = ShipmentStatus.Shipped,
                ShippedAt = order.PlacedAt,
            };
        }

        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return order.OrderNumber; // DB-assigned via Seq_OrderNumber, read back after insert
    }

    private static Task<HttpResponseMessage> PostJsonAsync(HttpClient client, string path, object body, string csrf)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-Token", csrf);
        return client.SendAsync(request);
    }

    private static string ExtractCookie(HttpResponseMessage response, string cookieName)
    {
        Assert.True(response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? cookies),
            $"Expected a Set-Cookie header carrying '{cookieName}'.");
        string? setCookie = cookies!.FirstOrDefault(c => c.StartsWith(cookieName + "=", StringComparison.Ordinal));
        Assert.NotNull(setCookie);
        string afterName = setCookie!.Substring(cookieName.Length + 1);
        int semicolon = afterName.IndexOf(';');
        return semicolon >= 0 ? afterName.Substring(0, semicolon) : afterName;
    }
}
