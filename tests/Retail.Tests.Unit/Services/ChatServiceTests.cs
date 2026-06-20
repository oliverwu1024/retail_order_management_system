using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Retail.Api.Ai;
using Retail.Api.Ai.Contracts;
using Retail.Api.Common.Models;
using Retail.Api.Domain.Entities;
using Retail.Api.DTOs.Requests;
using Retail.Api.DTOs.Responses;
using Retail.Api.Exceptions;
using Retail.Api.Repositories;
using Retail.Api.Services;

namespace Retail.Tests.Unit.Services;

/// <summary>
/// Unit tests for the two <see cref="ChatService"/> safety behaviours that the integration stub can't
/// easily force: the max-tool-turns loop cap, and graceful (HTTP-200) degradation when the AI
/// provider is down. Hand-rolled fakes (no Moq in this project).
/// </summary>
public class ChatServiceTests
{
    [Fact]
    public async Task HandleTurn_WhenModelLoopsForever_StopsAtMaxToolTurns()
    {
        // A provider that ALWAYS asks for a tool would loop without a cap.
        var alwaysToolUse = new FakeLlmClient(_ => new LlmCompletion(
            Text: null,
            ToolUses: new[] { new LlmToolUse("t", "list_my_recent_orders", EmptyArgs) },
            Usage: new LlmUsage(0, 0),
            StopReason: "tool_use"));

        ChatService service = Build(alwaysToolUse);

        ChatTurnDto turn = await service.HandleTurnAsync("user-1", NewRequest(), CancellationToken.None);

        Assert.Contains("finish", turn.Reply, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HandleTurn_WhenProviderThrows_ReturnsFriendlyReply()
    {
        var down = new FakeLlmClient(_ => throw new ExternalServiceException("provider down"));

        ChatService service = Build(down);

        // Must NOT throw (no 503) — degrade to a friendly message in-conversation.
        ChatTurnDto turn = await service.HandleTurnAsync("user-1", NewRequest(), CancellationToken.None);

        Assert.Contains("trouble", turn.Reply, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HandleTurn_WhenAToolThrows_RecoversAndReplies()
    {
        // First completion asks for a tool; the executor throws; the loop must survive (feeding the
        // model a generic failure) and the second completion finishes normally.
        int calls = 0;
        var llm = new FakeLlmClient(_ => ++calls == 1
            ? new LlmCompletion(null, new[] { new LlmToolUse("t", "get_order", EmptyArgs) }, new LlmUsage(0, 0), "tool_use")
            : new LlmCompletion("All sorted.", Array.Empty<LlmToolUse>(), new LlmUsage(0, 0), "end_turn"));

        var service = new ChatService(
            llm, new FakeChatRepository(), new ThrowingToolExecutor(), new FakeOrderQueryService(),
            new FakeCustomerProfileService(), TimeProvider.System, NullLogger<ChatService>.Instance);

        ChatTurnDto turn = await service.HandleTurnAsync("user-1", NewRequest(), CancellationToken.None);

        Assert.Equal("All sorted.", turn.Reply); // loop continued past the tool failure
    }

    private static readonly JsonElement EmptyArgs = JsonSerializer.SerializeToElement(new { });

    private static ChatWebhookRequest NewRequest() =>
        new() { ConversationId = Guid.NewGuid().ToString(), Message = "hello" };

    private static ChatService Build(ILlmClient llm) =>
        new(
            llm,
            new FakeChatRepository(),
            new FakeToolExecutor(),
            new FakeOrderQueryService(),
            new FakeCustomerProfileService(),
            TimeProvider.System,
            NullLogger<ChatService>.Instance);

    // ── fakes ───────────────────────────────────────────────────────────────────

    private sealed class FakeLlmClient : ILlmClient
    {
        private readonly Func<LlmRequest, LlmCompletion> _respond;
        public FakeLlmClient(Func<LlmRequest, LlmCompletion> respond) => _respond = respond;
        public Task<LlmCompletion> CompleteAsync(LlmRequest request, CancellationToken ct) => Task.FromResult(_respond(request));
    }

    private sealed class FakeChatRepository : IChatRepository
    {
        public Task<ChatSession?> GetSessionByConversationIdAsync(string conversationId, CancellationToken ct) =>
            Task.FromResult<ChatSession?>(null); // always a fresh session
        public Task<IReadOnlyList<ChatMessage>> ListMessagesAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ChatMessage>>(Array.Empty<ChatMessage>());
        public Task<ChatSession> CreateSessionResolvingRaceAsync(ChatSession session, CancellationToken ct) =>
            Task.FromResult(session); // our insert "wins" → same instance back
        public void AddMessage(ChatMessage message) { }
        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakeToolExecutor : IChatToolExecutor
    {
        public Task<string> ExecuteAsync(string appUserId, LlmToolUse toolUse, CancellationToken ct) => Task.FromResult("{}");
    }

    private sealed class ThrowingToolExecutor : IChatToolExecutor
    {
        public Task<string> ExecuteAsync(string appUserId, LlmToolUse toolUse, CancellationToken ct) =>
            throw new InvalidOperationException("tool blew up");
    }

    private sealed class FakeOrderQueryService : IOrderQueryService
    {
        public Task<PagedResult<OrderSummaryDto>> GetMyOrdersAsync(string appUserId, int page, int pageSize, CancellationToken ct) =>
            Task.FromResult(new PagedResult<OrderSummaryDto>(Array.Empty<OrderSummaryDto>(), 0, page, pageSize));
        public Task<OrderDetailDto> GetMyOrderAsync(string appUserId, Guid orderId, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrderDetailDto> GetOrderBySessionAsync(string stripeSessionId, CancellationToken ct) => throw new NotImplementedException();
    }

    private sealed class FakeCustomerProfileService : ICustomerProfileService
    {
        public Task<CustomerProfileDto> GetMyProfileAsync(string appUserId, CancellationToken ct) =>
            Task.FromResult(new CustomerProfileDto(Guid.NewGuid(), "e@test.local", "Tester", null, Array.Empty<AddressDto>()));
        public Task<CustomerProfileDto> UpdateMyProfileAsync(string appUserId, UpsertProfileRequest request, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<AddressDto>> ListMyAddressesAsync(string appUserId, CancellationToken ct) => throw new NotImplementedException();
        public Task<AddressDto> AddAddressAsync(string appUserId, AddressRequest request, CancellationToken ct) => throw new NotImplementedException();
        public Task<AddressDto> UpdateAddressAsync(string appUserId, Guid addressId, AddressRequest request, CancellationToken ct) => throw new NotImplementedException();
        public Task DeleteAddressAsync(string appUserId, Guid addressId, CancellationToken ct) => throw new NotImplementedException();
    }
}
