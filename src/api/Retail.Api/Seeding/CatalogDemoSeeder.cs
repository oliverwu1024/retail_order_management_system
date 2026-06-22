using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Retail.Api.Data;
using Retail.Api.Domain.Entities;

namespace Retail.Api.Seeding;

/// <summary>
/// DEVELOPMENT-ONLY demo catalog: seeds a small but realistic set of categories, published products,
/// active variants, and inventory so a fresh dev run has something to browse — and so the downstream
/// demo seeders (reviews, orders → anomaly scan → demand forecasting) have a catalog to build on.
/// Idempotent (skips if any product already exists) and never runs outside Development.
/// </summary>
/// <remarks>
/// Runs BEFORE <see cref="ReviewDemoSeeder"/> / <see cref="OrderDemoSeeder"/> (which require published
/// products / active variants). Prices span a wide range so the order seeder's injected big-total
/// anomaly clears the Z-score threshold against the modest normal baseline. No images are seeded (the
/// gallery is exercised separately); the storefront renders a placeholder.
/// </remarks>
public sealed class CatalogDemoSeeder
{
    private readonly RetailDbContext _db;
    private readonly IHostEnvironment _env;
    private readonly ILogger<CatalogDemoSeeder> _logger;

    public CatalogDemoSeeder(RetailDbContext db, IHostEnvironment env, ILogger<CatalogDemoSeeder> logger)
    {
        _db = db;
        _env = env;
        _logger = logger;
    }

    // Demo catalog definition: category → products → variants (option value + price in cents + on-hand).
    private static readonly CategorySeed[] Catalog =
    {
        new("Footwear", "footwear", new ProductSeed[]
        {
            new("Aero Runner", "AERO", "Featherweight road running shoe.", "size",
                new VariantSeed[] { new("8", 12900, 60), new("9", 12900, 40), new("10", 12900, 18), new("11", 12900, 120) }),
            new("Trail Blazer Boot", "TRBL", "Waterproof trail boot with a grippy outsole.", "size",
                new VariantSeed[] { new("8", 18900, 35), new("9", 18900, 22), new("10", 18900, 50) }),
        }),
        new("Apparel", "apparel", new ProductSeed[]
        {
            new("Merino Base Layer", "MERN", "Temperature-regulating merino long sleeve.", "size",
                new VariantSeed[] { new("S", 8900, 80), new("M", 8900, 25), new("L", 8900, 45) }),
            new("Storm Shell Jacket", "STSH", "Three-layer waterproof hardshell.", "size",
                new VariantSeed[] { new("S", 24900, 30), new("M", 24900, 12), new("L", 24900, 28) }),
            new("Performance Tee", "PERF", "Breathable quick-dry training tee.", "size",
                new VariantSeed[] { new("S", 3900, 200), new("M", 3900, 150), new("L", 3900, 90) }),
        }),
        new("Accessories", "accessories", new ProductSeed[]
        {
            new("Trail Cap", "TLCP", "Packable five-panel running cap.", "color",
                new VariantSeed[] { new("Black", 2900, 140), new("Sand", 2900, 60) }),
            new("Compression Socks", "CMSK", "Graduated-compression crew socks.", "size",
                new VariantSeed[] { new("M", 1900, 220), new("L", 1900, 30) }),
            new("Dry Bag 20L", "DRYB", "Roll-top waterproof stuff sack.", "size",
                new VariantSeed[] { new("20L", 4900, 75) }),
        }),
        new("Equipment", "equipment", new ProductSeed[]
        {
            new("Carbon Trek Pole", "CTPL", "Folding carbon-fibre trekking pole (pair).", "length",
                new VariantSeed[] { new("120cm", 14900, 40), new("130cm", 14900, 16) }),
            new("Hydration Vest 12L", "HYDV", "Race vest with soft flasks.", "size",
                new VariantSeed[] { new("S", 16900, 24), new("M", 16900, 10), new("L", 16900, 33) }),
        }),
    };

    public async Task SeedAsync(CancellationToken ct = default)
    {
        if (!_env.IsDevelopment() || await _db.Products.AnyAsync(ct))
        {
            return; // dev-only; idempotent (won't clobber an existing / manually-built catalog)
        }

        int products = 0, variants = 0;
        foreach (CategorySeed categorySeed in Catalog)
        {
            var category = new Category { Name = categorySeed.Name, Slug = categorySeed.Slug };
            _db.Categories.Add(category);

            foreach (ProductSeed productSeed in categorySeed.Products)
            {
                var product = new Product
                {
                    Category = category,
                    Sku = productSeed.SkuPrefix,
                    Slug = Slugify(productSeed.Name),
                    Name = productSeed.Name,
                    Description = productSeed.Description,
                    BrandName = "Trailhead",
                    IsPublished = true,
                };
                _db.Products.Add(product);
                products++;

                foreach (VariantSeed variantSeed in productSeed.Variants)
                {
                    var variant = new ProductVariant
                    {
                        Product = product,
                        Sku = $"{productSeed.SkuPrefix}-{variantSeed.OptionValue.ToUpperInvariant()}",
                        Options = new Dictionary<string, string> { [productSeed.OptionName] = variantSeed.OptionValue },
                        PriceCents = variantSeed.PriceCents,
                        IsActive = true,
                    };
                    // RowVersion is store-generated; OnHand drives the forecast reorder math.
                    variant.Inventory = new InventoryItem { Variant = variant, OnHand = variantSeed.OnHand };
                    _db.ProductVariants.Add(variant);
                    variants++;
                }
            }
        }

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation(
            "Catalog demo seed: created {Categories} categories, {Products} products, {Variants} variants (Development only).",
            Catalog.Length, products, variants);
    }

    private static string Slugify(string name) =>
        name.ToLowerInvariant().Replace(' ', '-').Replace("/", "-");

    private sealed record CategorySeed(string Name, string Slug, ProductSeed[] Products);

    private sealed record ProductSeed(string Name, string SkuPrefix, string Description, string OptionName, VariantSeed[] Variants);

    private sealed record VariantSeed(string OptionValue, int PriceCents, int OnHand);
}
