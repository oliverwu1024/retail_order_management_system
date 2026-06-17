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
    private readonly IValidator<UpdateProductImageRequest> _updateImageValidator;

    public CatalogController(
        ICatalogService catalog,
        IValidator<CreateCategoryRequest> createCategoryValidator,
        IValidator<CreateProductRequest> createProductValidator,
        IValidator<UpdateProductRequest> updateProductValidator,
        IValidator<CreateVariantRequest> createVariantValidator,
        IValidator<UpdateVariantRequest> updateVariantValidator,
        IValidator<UpdateProductImageRequest> updateImageValidator)
    {
        _catalog = catalog;
        _createCategoryValidator = createCategoryValidator;
        _createProductValidator = createProductValidator;
        _updateProductValidator = updateProductValidator;
        _createVariantValidator = createVariantValidator;
        _updateVariantValidator = updateVariantValidator;
        _updateImageValidator = updateImageValidator;
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

    /// <summary>
    /// Adds an image to a product's gallery (jpg/png/webp, ≤5 MB) → Blob. Optional <c>variantId</c>
    /// (variant-specific image) + <c>altText</c> form fields. First image becomes the primary.
    /// </summary>
    // RequestSizeLimit/RequestFormLimits reject an oversized body at the framework edge,
    // BEFORE model binding buffers it — so the 5 MB cap isn't only enforced post-buffer.
    [HttpPost("products/{id:guid}/images")]
    [Authorize(Roles = Roles.Administrator)]
    [RequestSizeLimit(ImageFormat.MaxBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = ImageFormat.MaxBytes)]
    [ProducesResponseType(typeof(ApiResponse<ProductDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> AddProductImage(
        Guid id, IFormFile file, [FromForm] Guid? variantId, [FromForm] string? altText, CancellationToken ct)
    {
        if (altText is { Length: > 200 })
        {
            return UnprocessableEntity(ApiResponse.Fail("Alt text must be 200 characters or fewer."));
        }

        (IActionResult? error, Stream? stream, string contentType) = await ValidateImageUploadAsync(file, ct);
        if (error is not null)
        {
            return error;
        }

        await using (stream!)
        {
            ProductDetailDto product = await _catalog.AddProductImageAsync(id, stream!, contentType, variantId, altText, ct);
            return Ok(ApiResponse<ProductDetailDto>.Ok(product));
        }
    }

    /// <summary>Reorders a product's gallery (body = the full set of image ids in display order).</summary>
    [HttpPut("products/{id:guid}/images/order")]
    [Authorize(Roles = Roles.Administrator)]
    [ProducesResponseType(typeof(ApiResponse<ProductDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ReorderProductImages(Guid id, [FromBody] ReorderProductImagesRequest request, CancellationToken ct)
    {
        ProductDetailDto product = await _catalog.ReorderProductImagesAsync(id, request.ImageIds ?? new List<Guid>(), ct);
        return Ok(ApiResponse<ProductDetailDto>.Ok(product));
    }

    /// <summary>Edits a gallery image (alt text, variant association, promote-to-primary).</summary>
    [HttpPut("products/{id:guid}/images/{imageId:guid}")]
    [Authorize(Roles = Roles.Administrator)]
    [ProducesResponseType(typeof(ApiResponse<ProductDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UpdateProductImage(Guid id, Guid imageId, [FromBody] UpdateProductImageRequest request, CancellationToken ct)
    {
        if (await ValidateAsync(_updateImageValidator, request, ct) is { } invalid)
        {
            return invalid;
        }

        ProductDetailDto product = await _catalog.UpdateProductImageAsync(id, imageId, request, ct);
        return Ok(ApiResponse<ProductDetailDto>.Ok(product));
    }

    /// <summary>Deletes a gallery image (promotes the next image to primary if it was the primary).</summary>
    [HttpDelete("products/{id:guid}/images/{imageId:guid}")]
    [Authorize(Roles = Roles.Administrator)]
    [ProducesResponseType(typeof(ApiResponse<ProductDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteProductImage(Guid id, Guid imageId, CancellationToken ct)
    {
        ProductDetailDto product = await _catalog.DeleteProductImageAsync(id, imageId, ct);
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

    /// <summary>
    /// Deactivates a variant (sets IsActive=false). It is NOT hard-deleted: orders and carts
    /// reference variants, so the row is preserved to keep that history intact. Reactivate it via
    /// the variant update endpoint (IsActive=true). Idempotent.
    /// </summary>
    [HttpDelete("products/{id:guid}/variants/{variantId:guid}")]
    [Authorize(Roles = Roles.Administrator)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteVariant(Guid id, Guid variantId, CancellationToken ct)
    {
        await _catalog.DeleteVariantAsync(id, variantId, ct);
        return Ok(ApiResponse.Ok("Variant deactivated."));
    }

    // ── helpers ───────────────────────────────────────────────────────────────────

    // Validates an uploaded image (size + the spoofable-content-type guard via a magic-byte sniff).
    // On success returns the stream rewound to 0 plus the DETECTED content type (never the
    // client-declared one); on failure returns a 422 in Error and disposes the stream.
    private async Task<(IActionResult? Error, Stream? Stream, string ContentType)> ValidateImageUploadAsync(
        IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
        {
            return (UnprocessableEntity(ApiResponse.Fail("An image file is required.")), null, string.Empty);
        }

        if (file.Length > ImageFormat.MaxBytes)
        {
            return (UnprocessableEntity(ApiResponse.Fail($"Image exceeds the {ImageFormat.MaxBytes / (1024 * 1024)} MB limit.")), null, string.Empty);
        }

        // Fast early reject on an obviously-wrong declared type, but the AUTHORITATIVE check is
        // the magic-byte sniff below — the client Content-Type is spoofable.
        if (!ImageFormat.IsAllowedContentType(file.ContentType))
        {
            return (UnprocessableEntity(ApiResponse.Fail("Only JPEG, PNG, or WebP images are allowed.")), null, string.Empty);
        }

        Stream stream = file.OpenReadStream();
        byte[] header = new byte[12];
        int read = await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, ct);
        if (!ImageFormat.TryDetectContentType(header.AsSpan(0, read), out string detectedContentType))
        {
            await stream.DisposeAsync();
            return (UnprocessableEntity(ApiResponse.Fail("The file is not a valid JPEG, PNG, or WebP image.")), null, string.Empty);
        }

        stream.Position = 0;
        return (null, stream, detectedContentType);
    }

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
