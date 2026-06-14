using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Retail.Api.Common.Constants;
using Retail.Api.Common.Helpers;
using Retail.Api.Common.Models;
using Retail.Api.DTOs.Requests;
using Retail.Api.DTOs.Responses;
using Retail.Api.Services;

namespace Retail.Api.Controllers;

/// <summary>
/// Catalogue endpoints (<c>/api/v1/catalog</c>): anonymous storefront reads + admin
/// writes. Reads only ever expose published, non-deleted products (the service +
/// soft-delete query filter enforce that); writes require the Administrator role.
/// </summary>
[ApiController]
[Route("api/v1/catalog")]
[Produces("application/json")]
public sealed class CatalogController : ControllerBase
{
    private readonly ICatalogService _catalog;
    private readonly IValidator<CreateCategoryRequest> _createCategoryValidator;
    private readonly IValidator<CreateProductRequest> _createProductValidator;
    private readonly IValidator<UpdateProductRequest> _updateProductValidator;
    private readonly IValidator<CreateVariantRequest> _createVariantValidator;
    private readonly IValidator<UpdateVariantRequest> _updateVariantValidator;

    public CatalogController(
        ICatalogService catalog,
        IValidator<CreateCategoryRequest> createCategoryValidator,
        IValidator<CreateProductRequest> createProductValidator,
        IValidator<UpdateProductRequest> updateProductValidator,
        IValidator<CreateVariantRequest> createVariantValidator,
        IValidator<UpdateVariantRequest> updateVariantValidator)
    {
        _catalog = catalog;
        _createCategoryValidator = createCategoryValidator;
        _createProductValidator = createProductValidator;
        _updateProductValidator = updateProductValidator;
        _createVariantValidator = createVariantValidator;
        _updateVariantValidator = updateVariantValidator;
    }

    // ── Public reads ────────────────────────────────────────────────────────────

    /// <summary>Paged catalogue listing — published products, optional category filter + name/description search.</summary>
    [HttpGet("products")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<ProductSummaryDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListProducts([FromQuery] ProductListQuery query, CancellationToken ct)
    {
        PagedResult<ProductSummaryDto> result = await _catalog.ListProductsAsync(query, ct);
        return Ok(ApiResponse<PagedResult<ProductSummaryDto>>.Ok(result));
    }

    /// <summary>Product detail by slug (published only; 404 otherwise).</summary>
    [HttpGet("products/{slug}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<ProductDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetProductBySlug(string slug, CancellationToken ct)
    {
        ProductDetailDto product = await _catalog.GetProductBySlugAsync(slug, ct);
        return Ok(ApiResponse<ProductDetailDto>.Ok(product));
    }

    /// <summary>All non-deleted categories.</summary>
    [HttpGet("categories")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<CategoryDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListCategories(CancellationToken ct)
    {
        IReadOnlyList<CategoryDto> categories = await _catalog.ListCategoriesAsync(ct);
        return Ok(ApiResponse<IReadOnlyList<CategoryDto>>.Ok(categories));
    }

    // ── Admin reads (all non-deleted, incl. unpublished; for the admin UI) ────────

    /// <summary>Paged product list for admin management — includes unpublished products.</summary>
    [HttpGet("admin/products")]
    [Authorize(Roles = Roles.Administrator)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<ProductSummaryDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListProductsForAdmin([FromQuery] ProductListQuery query, CancellationToken ct)
    {
        PagedResult<ProductSummaryDto> result = await _catalog.ListProductsForAdminAsync(query, ct);
        return Ok(ApiResponse<PagedResult<ProductSummaryDto>>.Ok(result));
    }

    /// <summary>Product detail by id for the admin edit form — works for unpublished products.</summary>
    [HttpGet("admin/products/{id:guid}")]
    [Authorize(Roles = Roles.Administrator)]
    [ProducesResponseType(typeof(ApiResponse<ProductDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetProductForAdmin(Guid id, CancellationToken ct)
    {
        ProductDetailDto product = await _catalog.GetProductForAdminAsync(id, ct);
        return Ok(ApiResponse<ProductDetailDto>.Ok(product));
    }

    // ── Admin writes ─────────────────────────────────────────────────────────────

    /// <summary>Creates a category.</summary>
    [HttpPost("categories")]
    [Authorize(Roles = Roles.Administrator)]
    [ProducesResponseType(typeof(ApiResponse<CategoryDto>), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateCategory([FromBody] CreateCategoryRequest request, CancellationToken ct)
    {
        if (await ValidateAsync(_createCategoryValidator, request, ct) is { } invalid)
        {
            return invalid;
        }

        CategoryDto category = await _catalog.CreateCategoryAsync(request, ct);
        return StatusCode(StatusCodes.Status201Created, ApiResponse<CategoryDto>.Ok(category));
    }

    /// <summary>Creates a product (variants added separately).</summary>
    [HttpPost("products")]
    [Authorize(Roles = Roles.Administrator)]
    [ProducesResponseType(typeof(ApiResponse<ProductDetailDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateProduct([FromBody] CreateProductRequest request, CancellationToken ct)
    {
        if (await ValidateAsync(_createProductValidator, request, ct) is { } invalid)
        {
            return invalid;
        }

        ProductDetailDto product = await _catalog.CreateProductAsync(request, ct);
        return StatusCode(StatusCodes.Status201Created, ApiResponse<ProductDetailDto>.Ok(product));
    }

    /// <summary>Updates a product's fields (not its SKU).</summary>
    [HttpPut("products/{id:guid}")]
    [Authorize(Roles = Roles.Administrator)]
    [ProducesResponseType(typeof(ApiResponse<ProductDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateProduct(Guid id, [FromBody] UpdateProductRequest request, CancellationToken ct)
    {
        if (await ValidateAsync(_updateProductValidator, request, ct) is { } invalid)
        {
            return invalid;
        }

        ProductDetailDto product = await _catalog.UpdateProductAsync(id, request, ct);
        return Ok(ApiResponse<ProductDetailDto>.Ok(product));
    }

    /// <summary>Soft-deletes a product (recoverable; hidden from the storefront).</summary>
    [HttpDelete("products/{id:guid}")]
    [Authorize(Roles = Roles.Administrator)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteProduct(Guid id, CancellationToken ct)
    {
        await _catalog.SoftDeleteProductAsync(id, ct);
        return Ok(ApiResponse.Ok("Product deleted."));
    }

    /// <summary>Uploads/replaces a product's primary image (jpg/png/webp, ≤5 MB) → Blob (Task 1.2.8).</summary>
    [HttpPost("products/{id:guid}/image")]
    [Authorize(Roles = Roles.Administrator)]
    [ProducesResponseType(typeof(ApiResponse<ProductDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UploadProductImage(Guid id, IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
        {
            return UnprocessableEntity(ApiResponse.Fail("An image file is required."));
        }

        if (file.Length > ProductImage.MaxBytes)
        {
            return UnprocessableEntity(ApiResponse.Fail($"Image exceeds the {ProductImage.MaxBytes / (1024 * 1024)} MB limit."));
        }

        if (!ProductImage.IsAllowedContentType(file.ContentType))
        {
            return UnprocessableEntity(ApiResponse.Fail("Only JPEG, PNG, or WebP images are allowed."));
        }

        await using Stream stream = file.OpenReadStream();
        ProductDetailDto product = await _catalog.SetProductPrimaryImageAsync(id, stream, file.ContentType, ct);
        return Ok(ApiResponse<ProductDetailDto>.Ok(product));
    }

    /// <summary>Adds a variant (and its initial stock) to a product.</summary>
    [HttpPost("products/{id:guid}/variants")]
    [Authorize(Roles = Roles.Administrator)]
    [ProducesResponseType(typeof(ApiResponse<ProductVariantDto>), StatusCodes.Status201Created)]
    public async Task<IActionResult> AddVariant(Guid id, [FromBody] CreateVariantRequest request, CancellationToken ct)
    {
        if (await ValidateAsync(_createVariantValidator, request, ct) is { } invalid)
        {
            return invalid;
        }

        ProductVariantDto variant = await _catalog.AddVariantAsync(id, request, ct);
        return StatusCode(StatusCodes.Status201Created, ApiResponse<ProductVariantDto>.Ok(variant));
    }

    /// <summary>Updates a variant's options/price/active flag.</summary>
    [HttpPut("products/{id:guid}/variants/{variantId:guid}")]
    [Authorize(Roles = Roles.Administrator)]
    [ProducesResponseType(typeof(ApiResponse<ProductVariantDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateVariant(Guid id, Guid variantId, [FromBody] UpdateVariantRequest request, CancellationToken ct)
    {
        if (await ValidateAsync(_updateVariantValidator, request, ct) is { } invalid)
        {
            return invalid;
        }

        ProductVariantDto variant = await _catalog.UpdateVariantAsync(id, variantId, request, ct);
        return Ok(ApiResponse<ProductVariantDto>.Ok(variant));
    }

    /// <summary>Deletes a variant from a product.</summary>
    [HttpDelete("products/{id:guid}/variants/{variantId:guid}")]
    [Authorize(Roles = Roles.Administrator)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteVariant(Guid id, Guid variantId, CancellationToken ct)
    {
        await _catalog.DeleteVariantAsync(id, variantId, ct);
        return Ok(ApiResponse.Ok("Variant deleted."));
    }

    // ── helpers ───────────────────────────────────────────────────────────────────

    // Runs the validator; returns a 422 result if invalid, or null to continue.
    private async Task<IActionResult?> ValidateAsync<T>(IValidator<T> validator, T request, CancellationToken ct)
    {
        ValidationResult result = await validator.ValidateAsync(request, ct);
        return result.IsValid
            ? null
            : UnprocessableEntity(ApiResponse.Fail("Validation failed.", ToApiErrors(result)));
    }

    private static IReadOnlyList<ApiError> ToApiErrors(ValidationResult validation) =>
        validation.Errors
            .Select(failure => new ApiError
            {
                Code = "VALIDATION_ERROR",
                Message = failure.ErrorMessage,
                Field = failure.PropertyName,
            })
            .ToList();
}
