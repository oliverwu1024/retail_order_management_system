using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Retail.Api.Data;
using Retail.Api.Domain.Entities;
using Retail.Api.HostedServices;

namespace Retail.Api.Seeding;

/// <summary>
/// DEVELOPMENT-ONLY demo data: seeds a handful of reviews with varied sentiment so the storefront
/// review section, the admin sentiment tile, and the Products-Needing-Attention panel show real data
/// on a fresh dev run (PLAN §8d demo bar). Idempotent (skips if any reviews already exist), never runs
/// outside Development, and skips gracefully if there are no published products to attach to.
/// </summary>
/// <remarks>
/// Reviews are inserted directly (bypassing the purchase-verified endpoint) and enqueued for sentiment
/// scoring; the hosted service scores them within seconds (the slow re-scan would also catch them).
/// </remarks>
public sealed class ReviewDemoSeeder
{
    private const int MaxProducts = 3;

    // Spans positive / mixed / negative / neutral so the dashboard distribution + attention panel vary.
    private static readonly (string Body, byte Rating)[] Samples =
    {
        ("Absolutely love this — excellent quality and great value. Highly recommend!", 5),
        ("Good product overall, though it arrived with a damaged box.", 4),
        ("Disappointed. It broke after a week and feels cheap — would not buy again.", 1),
        ("It's fine. Does the job, nothing special.", 3),
    };

    private readonly RetailDbContext _db;
    private readonly ReviewSentimentQueue _queue;
    private readonly IHostEnvironment _env;
    private readonly ILogger<ReviewDemoSeeder> _logger;

    public ReviewDemoSeeder(
        RetailDbContext db,
        ReviewSentimentQueue queue,
        IHostEnvironment env,
        ILogger<ReviewDemoSeeder> logger)
    {
        _db = db;
        _queue = queue;
        _env = env;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken ct = default)
    {
        if (!_env.IsDevelopment() || await _db.Reviews.AnyAsync(ct))
        {
            return; // dev-only; idempotent
        }

        List<Product> products = await _db.Products.AsNoTracking()
            .Where(p => p.IsPublished)
            .OrderBy(p => p.Name)
            .Take(MaxProducts)
            .ToListAsync(ct);
        if (products.Count == 0)
        {
            _logger.LogInformation("Review demo seed skipped: no published products to attach reviews to.");
            return;
        }

        var reviewIds = new List<Guid>();
        for (int sample = 0; sample < Samples.Length; sample++)
        {
            // One demo reviewer per sample (one review per customer per product is enforced), reviewing
            // each product — a spread of sentiment per product.
            (ApplicationUser user, CustomerProfile profile) = BuildDemoReviewer(sample);
            _db.Users.Add(user);
            _db.CustomerProfiles.Add(profile); // profile.Id is assigned now (client-generated GUID key)

            foreach (Product product in products)
            {
                var review = new Review
                {
                    ProductId = product.Id,             // existing product — reference by id, not nav
                    CustomerProfileId = profile.Id,
                    Rating = Samples[sample].Rating,
                    Body = Samples[sample].Body,
                };
                _db.Reviews.Add(review);
                reviewIds.Add(review.Id);
            }
        }

        await _db.SaveChangesAsync(ct);

        foreach (Guid id in reviewIds)
        {
            _queue.Enqueue(id);
        }
        _logger.LogInformation(
            "Review demo seed: created {Count} reviews across {Products} products (Development only).",
            reviewIds.Count, products.Count);
    }

    private static (ApplicationUser User, CustomerProfile Profile) BuildDemoReviewer(int index)
    {
        string email = $"demo-reviewer-{index}@demo.local";
        var user = new ApplicationUser
        {
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            DisplayName = $"Demo Reviewer {index + 1}",
            EmailConfirmed = true,
            SecurityStamp = Guid.NewGuid().ToString(),
        };
        var profile = new CustomerProfile { AppUserId = user.Id, DisplayName = user.DisplayName! };
        return (user, profile);
    }
}
