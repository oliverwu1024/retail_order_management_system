using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Retail.Api.Common.Enums;
using Retail.Api.Data;
using Retail.Api.Domain.Entities;

namespace Retail.Api.Seeding;

/// <summary>
/// DEVELOPMENT-ONLY demo data: seeds a couple of realistic support-chat conversations (with tool-call
/// rows) so the admin "Chat sessions" diagnostics page shows data on a fresh dev run (PHASE_5A_SCOPE
/// §15 C4). Idempotent (skips if any chat session already exists) and never runs outside Development.
/// </summary>
/// <remarks>
/// Sessions are inserted directly (not via the webhook) under a dedicated demo customer; the message
/// text/order numbers are illustrative — the seeder doesn't create matching orders.
/// </remarks>
public sealed class ChatDemoSeeder
{
    private readonly RetailDbContext _db;
    private readonly IHostEnvironment _env;
    private readonly TimeProvider _clock;
    private readonly ILogger<ChatDemoSeeder> _logger;

    public ChatDemoSeeder(
        RetailDbContext db,
        IHostEnvironment env,
        TimeProvider clock,
        ILogger<ChatDemoSeeder> logger)
    {
        _db = db;
        _env = env;
        _clock = clock;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken ct = default)
    {
        if (!_env.IsDevelopment() || await _db.ChatSessions.AnyAsync(ct))
        {
            return; // dev-only; idempotent
        }

        (ApplicationUser user, CustomerProfile profile) = BuildDemoCustomer();
        _db.Users.Add(user);
        _db.CustomerProfiles.Add(profile); // profile.Id is assigned now (client-generated GUID key)

        DateTimeOffset now = _clock.GetUtcNow();
        for (int i = 0; i < Conversations.Length; i++)
        {
            // Stagger StartedAt/LastMessageAt per session so the diagnostics list (ordered by
            // LastMessageAt) is deterministic rather than tied on one shared timestamp.
            DateTimeOffset startedAt = now.AddMinutes(-(Conversations.Length - i) * 5);
            var session = new ChatSession
            {
                CustomerProfileId = profile.Id,
                ConversationId = Guid.NewGuid().ToString(),
                StartedAt = startedAt,
                LastMessageAt = startedAt.AddMinutes(2),
            };
            _db.ChatSessions.Add(session);

            foreach (DemoMessage m in Conversations[i].Messages)
            {
                _db.ChatMessages.Add(new ChatMessage
                {
                    ChatSession = session,
                    Role = m.Role,
                    Content = m.Content,
                    ToolName = m.ToolName,
                    ToolPayloadJson = m.ToolPayloadJson,
                });
            }
        }

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation(
            "Chat demo seed: created {Count} demo chat sessions (Development only).", Conversations.Length);
    }

    private static (ApplicationUser User, CustomerProfile Profile) BuildDemoCustomer()
    {
        const string email = "demo-chat@demo.local";
        var user = new ApplicationUser
        {
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            DisplayName = "Demo Chat Customer",
            EmailConfirmed = true,
            SecurityStamp = Guid.NewGuid().ToString(),
        };
        var profile = new CustomerProfile { AppUserId = user.Id, DisplayName = user.DisplayName! };
        return (user, profile);
    }

    private sealed record DemoMessage(
        ChatMessageRole Role, string Content, string? ToolName = null, string? ToolPayloadJson = null);

    // Two illustrative transcripts: an order lookup, and a confirmation-gated return proposal.
    private static readonly (string Topic, DemoMessage[] Messages)[] Conversations =
    {
        ("Order lookup", new[]
        {
            new DemoMessage(ChatMessageRole.User, "Hi — can you show me my recent orders?"),
            new DemoMessage(ChatMessageRole.Tool, "Called list_my_recent_orders.", "list_my_recent_orders",
                "{\"orders\":[{\"orderNumber\":10005,\"status\":\"Paid\",\"placedAt\":\"2026-06-18\",\"totalCents\":4200,\"itemCount\":1}]}"),
            new DemoMessage(ChatMessageRole.Assistant,
                "Sure! Your most recent order is #10005 (Paid, placed 2026-06-18) for $42.00. Anything else I can help with?"),
        }),
        ("Return request", new[]
        {
            new DemoMessage(ChatMessageRole.User, "I'd like to cancel order 10005 for a refund."),
            new DemoMessage(ChatMessageRole.Tool, "Called start_return.", "start_return",
                "{\"found\":true,\"eligible\":true,\"orderNumber\":10005,\"refundAmountCents\":4200}"),
            new DemoMessage(ChatMessageRole.Assistant,
                "Order #10005 is eligible for cancellation and a full refund of $42.00 — just hit Confirm and I'll take care of it."),
        }),
    };
}
