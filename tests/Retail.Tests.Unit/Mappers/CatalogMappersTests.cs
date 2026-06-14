using Retail.Api.Domain.Entities;
using Retail.Api.DTOs.Responses;
using Retail.Api.Mappers;

namespace Retail.Tests.Unit.Mappers;

/// <summary>Unit tests for catalogue entity → DTO mapping (stock-status buckets, "from" pricing).</summary>
public class CatalogMappersTests
{
    private static ProductVariant Variant(int onHand, int priceCents = 1000, bool isActive = true) =>
        new()
        {
            Sku = "v",
            PriceCents = priceCents,
            IsActive = isActive,
            Inventory = new InventoryItem { OnHand = onHand, Reserved = 0 },
        };

    [Theory]
    [InlineData(0, "OutOfStock")]
    [InlineData(1, "LowStock")]
    [InlineData(9, "LowStock")]
    [InlineData(10, "InStock")]
    [InlineData(100, "InStock")]
    public void VariantToDto_MapsStockStatusByThreshold(int available, string expectedStatus)
    {
        ProductVariantDtoAssert(Variant(onHand: available).ToDto(), available, expectedStatus);
    }

    [Fact]
    public void VariantToDto_WithNullInventory_TreatedAsOutOfStock()
    {
        var variant = new ProductVariant { Sku = "v", PriceCents = 1000, IsActive = true, Inventory = null };

        ProductVariantDtoAssert(variant.ToDto(), available: 0, expectedStatus: "OutOfStock");
    }

    [Fact]
    public void ProductToSummaryDto_FromPriceIsCheapestActiveVariant()
    {
        var product = new Product { Sku = "p", Slug = "p", Name = "P", CategoryId = Guid.NewGuid() };
        product.Variants.Add(Variant(onHand: 5, priceCents: 3000));
        product.Variants.Add(Variant(onHand: 5, priceCents: 1500)); // cheapest active
        product.Variants.Add(Variant(onHand: 5, priceCents: 500, isActive: false)); // inactive → ignored

        Assert.Equal(1500, product.ToSummaryDto().FromPriceCents);
    }

    [Fact]
    public void ProductToSummaryDto_WithNoActiveVariants_FromPriceIsNull()
    {
        var product = new Product { Sku = "p", Slug = "p", Name = "P", CategoryId = Guid.NewGuid() };
        product.Variants.Add(Variant(onHand: 5, priceCents: 500, isActive: false));

        Assert.Null(product.ToSummaryDto().FromPriceCents);
    }

    private static void ProductVariantDtoAssert(ProductVariantDto dto, int available, string expectedStatus)
    {
        Assert.Equal(available, dto.Available);
        Assert.Equal(expectedStatus, dto.StockStatus);
    }
}
