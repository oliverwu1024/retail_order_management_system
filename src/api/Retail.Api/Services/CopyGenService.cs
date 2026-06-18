using System.Text.Json;
using Retail.Api.Ai;
using Retail.Api.Ai.Contracts;
using Retail.Api.Domain.Entities;
using Retail.Api.DTOs.Requests;
using Retail.Api.DTOs.Responses;
using Retail.Api.Exceptions;
using Retail.Api.Repositories;

namespace Retail.Api.Services;

/// <summary>
/// Generates product copy via <see cref="ILlmClient"/> (ADR-0005). Forces the <c>emit_product_copy</c>
/// tool so the model must return structured JSON, then maps it to the response DTO. Depends only on
/// the abstraction — the provider (Anthropic live / stub) is chosen at DI time by <c>Ai:Mode</c>.
/// </summary>
public sealed class CopyGenService : ICopyGenService
{
    private const string EmitToolName = "emit_product_copy";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    // JSON Schema for the forced tool — the model's output must match this shape exactly.
    private static readonly JsonElement EmitCopySchema = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            description = new { type = "string", description = "Marketing product description, 2-4 sentences." },
            seoTitle = new { type = "string", description = "SEO <title>, <= 60 characters." },
            seoMetaDescription = new { type = "string", description = "SEO meta description, <= 160 characters." },
            bulletPoints = new { type = "array", items = new { type = "string" }, description = "3-5 short selling points." },
        },
        required = new[] { "description", "seoTitle", "seoMetaDescription", "bulletPoints" },
    });

    private readonly ILlmClient _llm;
    private readonly IProductRepository _products;
    private readonly ILogger<CopyGenService> _logger;

    public CopyGenService(ILlmClient llm, IProductRepository products, ILogger<CopyGenService> logger)
    {
        _llm = llm;
        _products = products;
        _logger = logger;
    }

    public async Task<SuggestProductCopyResponse> GenerateAsync(Guid productId, SuggestDescriptionRequest request, CancellationToken ct)
    {
        Product product = await _products.GetDetailByIdAsync(productId, ct)
            ?? throw new NotFoundException($"Product '{productId}' was not found.");

        var emitTool = new LlmTool(EmitToolName, "Emit the generated product description and SEO copy.", EmitCopySchema);

        var llmRequest = new LlmRequest(
            Model: "copy", // logical name; the provider resolves it to the real model id
            SystemPrompt: BuildSystemPrompt(request.Tone),
            Messages: [new LlmMessage(LlmRole.User, Text: BuildUserPrompt(product, request))],
            Tools: [emitTool],
            ToolChoice: LlmToolChoice.RequiredTool(EmitToolName), // guarantees structured output
            MaxTokens: 1024);

        LlmCompletion completion = await _llm.CompleteAsync(llmRequest, ct);

        LlmToolUse toolUse = completion.ToolUses.FirstOrDefault()
            ?? throw new ExternalServiceException("The AI provider did not return the expected structured output.");

        _logger.LogInformation(
            "CopyGen used {InputTokens} input + {OutputTokens} output tokens for product {ProductId}",
            completion.Usage.InputTokens, completion.Usage.OutputTokens, productId);

        return JsonSerializer.Deserialize<SuggestProductCopyResponse>(toolUse.Input.GetRawText(), JsonOptions)
            ?? throw new ExternalServiceException("The AI provider returned copy that could not be parsed.");
    }

    private static string BuildSystemPrompt(string tone) =>
        $"You are an expert e-commerce copywriter. Write in a {tone} tone — concise, benefit-led, and "
        + "honest. Always call the emit_product_copy tool; never include a price or invent specs you "
        + "were not given. Quality bar examples:\n"
        + "- description: \"Brewed for slow mornings, this double-walled tumbler keeps coffee hot for hours "
        + "without scalding your hands.\"\n"
        + "- bulletPoint: \"Keeps drinks hot for up to 6 hours\".";

    private static string BuildUserPrompt(Product product, SuggestDescriptionRequest request)
    {
        string category = product.Category?.Name ?? "general";
        string brand = string.IsNullOrWhiteSpace(product.BrandName) ? "unbranded" : product.BrandName!;
        return $"Write {request.Length}-length product copy for:\n"
            + $"Name: {product.Name}\n"
            + $"Category: {category}\n"
            + $"Brand: {brand}";
    }
}
