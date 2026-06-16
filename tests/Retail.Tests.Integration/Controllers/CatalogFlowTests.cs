using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Retail.Tests.Integration.Controllers;

/// <summary>
/// End-to-end catalogue tests through the full pipeline on real SQL Server: admin
/// CRUD, storefront reads, soft-delete visibility, RBAC gating, validation, and
/// conflict/not-found mapping.
/// </summary>
[Collection("api")]
public class CatalogFlowTests
{
    private readonly ApiFactory _factory;

    public CatalogFlowTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task AdminCreatesPublishedProduct_StorefrontSeesIt_UpdateApplies_SoftDeleteHidesIt()
    {
        (HttpClient admin, string csrf) = await AdminClientAsync();
        string suffix = Guid.NewGuid().ToString("N")[..8];

        string categoryId = await CreateCategoryAsync(admin, csrf, $"Cat {suffix}");

        string sku = $"SKU-{suffix}";
        HttpResponseMessage createResp = await PostJsonAsync(admin, "/api/v1/catalog/products", new
        {
            sku,
            name = $"Widget {suffix}",
            slug = (string?)null,
            description = "A fine widget.",
            brandName = "Acme",
            categoryId,
            isPublished = true,
        }, csrf);
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
        JsonElement created = (await createResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        string productId = created.GetProperty("id").GetString()!;
        string slug = created.GetProperty("slug").GetString()!;

        HttpResponseMessage variantResp = await PostJsonAsync(admin, $"/api/v1/catalog/products/{productId}/variants", new
        {
            sku = $"VAR-{suffix}",
            options = new Dictionary<string, string> { ["size"] = "M" },
            priceCents = 1999,
            compareAtPriceCents = (int?)2499,
            initialStock = 5,
        }, csrf);
        Assert.Equal(HttpStatusCode.Created, variantResp.StatusCode);

        // Storefront listing (anonymous) shows the published product.
        HttpClient anon = _factory.CreateClient();
        JsonElement list = (await (await anon.GetAsync("/api/v1/catalog/products?pageSize=100"))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        Assert.Contains(
            list.GetProperty("items").EnumerateArray(),
            i => i.GetProperty("sku").GetString() == sku);

        // Detail by slug carries the variant + stock indicator (5 < 10 → LowStock).
        HttpResponseMessage detailResp = await anon.GetAsync($"/api/v1/catalog/products/{slug}");
        Assert.Equal(HttpStatusCode.OK, detailResp.StatusCode);
        JsonElement detail = (await detailResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        JsonElement variant = detail.GetProperty("variants").EnumerateArray().Single();
        Assert.Equal(5, variant.GetProperty("available").GetInt32());
        Assert.Equal("LowStock", variant.GetProperty("stockStatus").GetString());

        // Update renames the product.
        HttpResponseMessage updateResp = await PutJsonAsync(admin, $"/api/v1/catalog/products/{productId}", new
        {
            name = $"Renamed {suffix}",
            slug,
            description = "Updated.",
            categoryId,
            isPublished = true,
        }, csrf);
        Assert.Equal(HttpStatusCode.OK, updateResp.StatusCode);
        JsonElement reread = (await (await anon.GetAsync($"/api/v1/catalog/products/{slug}"))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        Assert.Equal($"Renamed {suffix}", reread.GetProperty("name").GetString());

        // Soft-delete hides it from the storefront.
        Assert.Equal(HttpStatusCode.OK, (await DeleteAsync(admin, $"/api/v1/catalog/products/{productId}", csrf)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync($"/api/v1/catalog/products/{slug}")).StatusCode);
    }

    [Fact]
    public async Task AnonymousWrite_Returns401()
    {
        HttpClient anon = _factory.CreateClient();

        HttpResponseMessage resp = await anon.PostAsJsonAsync(
            "/api/v1/catalog/products",
            new { sku = "X", name = "X", categoryId = Guid.NewGuid(), isPublished = false });

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task NonAdminWrite_Returns403()
    {
        (HttpClient customer, string csrf) = await CustomerClientAsync();

        HttpResponseMessage resp = await PostJsonAsync(customer, "/api/v1/catalog/products",
            new { sku = "X", name = "X", categoryId = Guid.NewGuid(), isPublished = false }, csrf);

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task CreateProduct_DuplicateSku_Returns409()
    {
        (HttpClient admin, string csrf) = await AdminClientAsync();
        string suffix = Guid.NewGuid().ToString("N")[..8];
        string categoryId = await CreateCategoryAsync(admin, csrf, $"Cat {suffix}");
        string sku = $"DUP-{suffix}";

        object body = new { sku, name = $"First {suffix}", categoryId, isPublished = false };
        Assert.Equal(HttpStatusCode.Created,
            (await PostJsonAsync(admin, "/api/v1/catalog/products", body, csrf)).StatusCode);

        // Same SKU, different name → conflict.
        HttpResponseMessage dup = await PostJsonAsync(admin, "/api/v1/catalog/products",
            new { sku, name = $"Second {suffix}", categoryId, isPublished = false }, csrf);
        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);
    }

    [Fact]
    public async Task CreateProduct_InvalidBody_Returns422()
    {
        (HttpClient admin, string csrf) = await AdminClientAsync();

        HttpResponseMessage resp = await PostJsonAsync(admin, "/api/v1/catalog/products",
            new { sku = "", name = "", categoryId = Guid.Empty, isPublished = false }, csrf);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    [Fact]
    public async Task GetProduct_UnknownSlug_Returns404()
    {
        HttpClient anon = _factory.CreateClient();

        HttpResponseMessage resp = await anon.GetAsync($"/api/v1/catalog/products/nope-{Guid.NewGuid():N}");

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task AdminUploadsImage_AddsToGallery_AndSetsPrimary()
    {
        (HttpClient admin, string csrf) = await AdminClientAsync();
        string suffix = Guid.NewGuid().ToString("N")[..8];
        string productId = await CreateProductAsync(admin, csrf, suffix);

        HttpResponseMessage resp = await UploadImageAsync(
            admin, productId, MinimalPng(), "image/png", "p.png", csrf, altText: "front view");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        JsonElement data = await DataAsync(resp);
        string? key = data.GetProperty("primaryImageBlobKey").GetString();
        Assert.StartsWith("products/", key);
        Assert.EndsWith(".png", key);

        List<JsonElement> images = Images(data);
        Assert.Single(images);
        Assert.True(images[0].GetProperty("isPrimary").GetBoolean());
        Assert.Equal("front view", images[0].GetProperty("altText").GetString());
        Assert.Equal(key, images[0].GetProperty("blobKey").GetString()); // cache mirrors the primary
    }

    [Fact]
    public async Task Gallery_AddTwoImages_OnlyFirstIsPrimary_WithDistinctSortOrder()
    {
        (HttpClient admin, string csrf) = await AdminClientAsync();
        string suffix = Guid.NewGuid().ToString("N")[..8];
        string productId = await CreateProductAsync(admin, csrf, suffix);

        await AddImageAsync(admin, productId, csrf, "a.png");
        HttpResponseMessage resp = await UploadImageAsync(admin, productId, MinimalPng(), "image/png", "b.png", csrf);
        List<JsonElement> images = Images(await DataAsync(resp));

        Assert.Equal(2, images.Count);
        Assert.Equal(1, images.Count(i => i.GetProperty("isPrimary").GetBoolean())); // exactly one primary
        Assert.Equal(new[] { 0, 1 }, images.Select(i => i.GetProperty("sortOrder").GetInt32()).OrderBy(x => x));
    }

    [Fact]
    public async Task Gallery_SetPrimary_SwapsPrimaryAndUpdatesCache()
    {
        (HttpClient admin, string csrf) = await AdminClientAsync();
        string suffix = Guid.NewGuid().ToString("N")[..8];
        string productId = await CreateProductAsync(admin, csrf, suffix);
        await AddImageAsync(admin, productId, csrf, "a.png");
        string secondId = await AddImageAsync(admin, productId, csrf, "b.png");

        HttpResponseMessage resp = await PutJsonAsync(
            admin, $"/api/v1/catalog/products/{productId}/images/{secondId}", new { isPrimary = true }, csrf);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        JsonElement data = await DataAsync(resp);
        JsonElement promoted = Images(data).First(i => i.GetProperty("id").GetString() == secondId);
        Assert.True(promoted.GetProperty("isPrimary").GetBoolean());
        Assert.Equal(1, Images(data).Count(i => i.GetProperty("isPrimary").GetBoolean()));
        Assert.Equal(promoted.GetProperty("blobKey").GetString(), data.GetProperty("primaryImageBlobKey").GetString());
    }

    [Fact]
    public async Task Gallery_Reorder_ReassignsSortOrder()
    {
        (HttpClient admin, string csrf) = await AdminClientAsync();
        string suffix = Guid.NewGuid().ToString("N")[..8];
        string productId = await CreateProductAsync(admin, csrf, suffix);
        string firstId = await AddImageAsync(admin, productId, csrf, "a.png");
        string secondId = await AddImageAsync(admin, productId, csrf, "b.png");

        // Reverse the order.
        HttpResponseMessage resp = await PutJsonAsync(
            admin, $"/api/v1/catalog/products/{productId}/images/order", new { imageIds = new[] { secondId, firstId } }, csrf);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        List<JsonElement> images = Images(await DataAsync(resp));
        Assert.Equal(0, images.First(i => i.GetProperty("id").GetString() == secondId).GetProperty("sortOrder").GetInt32());
        Assert.Equal(1, images.First(i => i.GetProperty("id").GetString() == firstId).GetProperty("sortOrder").GetInt32());
    }

    [Fact]
    public async Task Gallery_DeletePrimary_PromotesNext_AndUpdatesCache()
    {
        (HttpClient admin, string csrf) = await AdminClientAsync();
        string suffix = Guid.NewGuid().ToString("N")[..8];
        string productId = await CreateProductAsync(admin, csrf, suffix);
        string firstId = await AddImageAsync(admin, productId, csrf, "a.png"); // becomes primary
        string secondId = await AddImageAsync(admin, productId, csrf, "b.png");

        HttpResponseMessage resp = await DeleteAsync(
            admin, $"/api/v1/catalog/products/{productId}/images/{firstId}", csrf);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        JsonElement data = await DataAsync(resp);
        List<JsonElement> images = Images(data);
        Assert.Single(images);
        Assert.Equal(secondId, images[0].GetProperty("id").GetString());
        Assert.True(images[0].GetProperty("isPrimary").GetBoolean()); // promoted
        Assert.Equal(images[0].GetProperty("blobKey").GetString(), data.GetProperty("primaryImageBlobKey").GetString());
    }

    [Fact]
    public async Task Gallery_UploadWithVariant_AssociatesImageToVariant()
    {
        (HttpClient admin, string csrf) = await AdminClientAsync();
        string suffix = Guid.NewGuid().ToString("N")[..8];
        string productId = await CreateProductAsync(admin, csrf, suffix);
        string variantId = await AddVariantAsync(admin, productId, csrf, suffix);

        HttpResponseMessage resp = await UploadImageAsync(
            admin, productId, MinimalPng(), "image/png", "v.png", csrf, variantId: Guid.Parse(variantId));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        JsonElement image = Images(await DataAsync(resp)).Single();
        Assert.Equal(variantId, image.GetProperty("productVariantId").GetString());
    }

    [Fact]
    public async Task Gallery_UploadWithUnknownVariant_Returns404()
    {
        (HttpClient admin, string csrf) = await AdminClientAsync();
        string suffix = Guid.NewGuid().ToString("N")[..8];
        string productId = await CreateProductAsync(admin, csrf, suffix);

        HttpResponseMessage resp = await UploadImageAsync(
            admin, productId, MinimalPng(), "image/png", "x.png", csrf, variantId: Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task UploadImage_DisallowedContentType_Returns422()
    {
        (HttpClient admin, string csrf) = await AdminClientAsync();
        string suffix = Guid.NewGuid().ToString("N")[..8];
        string productId = await CreateProductAsync(admin, csrf, suffix);

        HttpResponseMessage resp = await UploadImageAsync(
            admin, productId, Encoding.UTF8.GetBytes("not an image"), "text/plain", "p.txt", csrf);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    [Fact]
    public async Task AdminProductList_IncludesUnpublished_WhilePublicListExcludesIt()
    {
        (HttpClient admin, string csrf) = await AdminClientAsync();
        string suffix = Guid.NewGuid().ToString("N")[..8];
        (string _, string sku) = await CreateUnpublishedProductAsync(admin, csrf, suffix);

        JsonElement adminList = (await (await admin.GetAsync("/api/v1/catalog/admin/products?pageSize=100"))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        Assert.Contains(adminList.GetProperty("items").EnumerateArray(), i => i.GetProperty("sku").GetString() == sku);

        HttpClient anon = _factory.CreateClient();
        JsonElement publicList = (await (await anon.GetAsync("/api/v1/catalog/products?pageSize=100"))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        Assert.DoesNotContain(publicList.GetProperty("items").EnumerateArray(), i => i.GetProperty("sku").GetString() == sku);
    }

    [Fact]
    public async Task AdminGetById_ReturnsUnpublishedProduct()
    {
        (HttpClient admin, string csrf) = await AdminClientAsync();
        string suffix = Guid.NewGuid().ToString("N")[..8];
        (string productId, string sku) = await CreateUnpublishedProductAsync(admin, csrf, suffix);

        HttpResponseMessage resp = await admin.GetAsync($"/api/v1/catalog/admin/products/{productId}");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        JsonElement data = (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        Assert.Equal(sku, data.GetProperty("sku").GetString());
        Assert.False(data.GetProperty("isPublished").GetBoolean());
    }

    [Fact]
    public async Task AdminProductList_Anonymous_Returns401()
    {
        HttpClient anon = _factory.CreateClient();

        HttpResponseMessage resp = await anon.GetAsync("/api/v1/catalog/admin/products");

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task DeleteVariant_WithVariantImage_Succeeds_AndReHomesImageToGeneral()
    {
        (HttpClient admin, string csrf) = await AdminClientAsync();
        string suffix = Guid.NewGuid().ToString("N")[..8];
        string productId = await CreateProductAsync(admin, csrf, suffix);
        string variantId = await AddVariantAsync(admin, productId, csrf, suffix);
        (await UploadImageAsync(admin, productId, MinimalPng(), "image/png", "v.png", csrf, variantId: Guid.Parse(variantId)))
            .EnsureSuccessStatusCode();

        // Before the fix this 500'd (variant→image FK is NoAction and the images weren't detached).
        HttpResponseMessage del = await DeleteAsync(admin, $"/api/v1/catalog/products/{productId}/variants/{variantId}", csrf);
        Assert.Equal(HttpStatusCode.OK, del.StatusCode);

        // The image survives, re-homed to general (productVariantId null).
        JsonElement detail = await DataAsync(await admin.GetAsync($"/api/v1/catalog/admin/products/{productId}"));
        List<JsonElement> images = Images(detail);
        Assert.Single(images);
        // General image = productVariantId is null (the serializer omits null properties).
        bool isGeneral = !images[0].TryGetProperty("productVariantId", out JsonElement pv)
            || pv.ValueKind == JsonValueKind.Null;
        Assert.True(isGeneral, "the re-homed image should have no variant association");
    }

    [Fact]
    public async Task UpdateImage_AltTextTooLong_Returns422()
    {
        (HttpClient admin, string csrf) = await AdminClientAsync();
        string suffix = Guid.NewGuid().ToString("N")[..8];
        string productId = await CreateProductAsync(admin, csrf, suffix);
        string imageId = await AddImageAsync(admin, productId, csrf, "a.png");

        HttpResponseMessage resp = await PutJsonAsync(
            admin, $"/api/v1/catalog/products/{productId}/images/{imageId}",
            new { altText = new string('x', 201) }, csrf);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    [Fact]
    public async Task AddImage_AltTextTooLong_Returns422()
    {
        (HttpClient admin, string csrf) = await AdminClientAsync();
        string suffix = Guid.NewGuid().ToString("N")[..8];
        string productId = await CreateProductAsync(admin, csrf, suffix);

        HttpResponseMessage resp = await UploadImageAsync(
            admin, productId, MinimalPng(), "image/png", "a.png", csrf, altText: new string('x', 201));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    // ── helpers ───────────────────────────────────────────────────────────────────

    private async Task<(HttpClient Client, string Csrf)> AdminClientAsync()
    {
        HttpClient client = _factory.CreateClient();
        string csrf = ExtractCookie(await client.GetAsync("/api/v1/auth/csrf"), "csrf");
        HttpResponseMessage login = await PostJsonAsync(client, "/api/v1/auth/login",
            new { email = "admin@test.local", password = "TestAdmin123456" }, csrf);
        login.EnsureSuccessStatusCode();
        return (client, ExtractCookie(login, "csrf")); // login re-issues the csrf token
    }

    private async Task<(HttpClient Client, string Csrf)> CustomerClientAsync()
    {
        HttpClient client = _factory.CreateClient();
        string csrf = ExtractCookie(await client.GetAsync("/api/v1/auth/csrf"), "csrf");
        HttpResponseMessage register = await PostJsonAsync(client, "/api/v1/auth/register",
            new { email = $"cust-{Guid.NewGuid():N}@test.local", password = "Sup3rSecret!pw", displayName = "Cust" }, csrf);
        register.EnsureSuccessStatusCode();
        return (client, ExtractCookie(register, "csrf"));
    }

    private static async Task<string> CreateCategoryAsync(HttpClient admin, string csrf, string name)
    {
        HttpResponseMessage resp = await PostJsonAsync(admin, "/api/v1/catalog/categories",
            new { name, slug = (string?)null, parentId = (Guid?)null }, csrf);
        resp.EnsureSuccessStatusCode();
        JsonElement body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("data").GetProperty("id").GetString()!;
    }

    private static async Task<string> CreateProductAsync(HttpClient admin, string csrf, string suffix)
    {
        string categoryId = await CreateCategoryAsync(admin, csrf, $"Cat {suffix}");
        HttpResponseMessage resp = await PostJsonAsync(admin, "/api/v1/catalog/products",
            new { sku = $"SKU-{suffix}", name = $"Product {suffix}", categoryId, isPublished = true }, csrf);
        resp.EnsureSuccessStatusCode();
        JsonElement body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("data").GetProperty("id").GetString()!;
    }

    private static async Task<(string Id, string Sku)> CreateUnpublishedProductAsync(HttpClient admin, string csrf, string suffix)
    {
        string categoryId = await CreateCategoryAsync(admin, csrf, $"Cat {suffix}");
        string sku = $"SKU-{suffix}";
        HttpResponseMessage resp = await PostJsonAsync(admin, "/api/v1/catalog/products",
            new { sku, name = $"Draft {suffix}", categoryId, isPublished = false }, csrf);
        resp.EnsureSuccessStatusCode();
        JsonElement body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return (body.GetProperty("data").GetProperty("id").GetString()!, sku);
    }

    private static Task<HttpResponseMessage> UploadImageAsync(
        HttpClient client, string productId, byte[] bytes, string contentType, string fileName, string csrf,
        Guid? variantId = null, string? altText = null)
    {
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        var form = new MultipartFormDataContent { { fileContent, "file", fileName } };
        if (variantId is Guid vid)
        {
            form.Add(new StringContent(vid.ToString()), "variantId");
        }
        if (altText is not null)
        {
            form.Add(new StringContent(altText), "altText");
        }

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/catalog/products/{productId}/images") { Content = form };
        request.Headers.Add("X-CSRF-Token", csrf);
        return client.SendAsync(request);
    }

    private static async Task<JsonElement> DataAsync(HttpResponseMessage resp) =>
        (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");

    private static List<JsonElement> Images(JsonElement data) =>
        data.GetProperty("images").EnumerateArray().ToList();

    // Uploads a general image and returns the new image's id (last by sortOrder).
    private static async Task<string> AddImageAsync(HttpClient admin, string productId, string csrf, string fileName)
    {
        HttpResponseMessage resp = await UploadImageAsync(admin, productId, MinimalPng(), "image/png", fileName, csrf);
        resp.EnsureSuccessStatusCode();
        JsonElement data = await DataAsync(resp);
        return Images(data).OrderByDescending(i => i.GetProperty("sortOrder").GetInt32())
            .First().GetProperty("id").GetString()!;
    }

    private static async Task<string> AddVariantAsync(HttpClient admin, string productId, string csrf, string suffix)
    {
        HttpResponseMessage resp = await PostJsonAsync(admin, $"/api/v1/catalog/products/{productId}/variants",
            new
            {
                sku = $"VAR-{suffix}",
                options = new Dictionary<string, string> { ["size"] = "M" },
                priceCents = 1999,
                compareAtPriceCents = (int?)null,
                initialStock = 5,
            }, csrf);
        resp.EnsureSuccessStatusCode();
        return (await DataAsync(resp)).GetProperty("id").GetString()!;
    }

    // A 1x1 transparent PNG — enough to exercise the upload path (the endpoint validates
    // content type + size, not the pixel data).
    private static byte[] MinimalPng() =>
        Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    private static Task<HttpResponseMessage> PostJsonAsync(HttpClient client, string path, object body, string csrf) =>
        SendJsonAsync(client, HttpMethod.Post, path, body, csrf);

    private static Task<HttpResponseMessage> PutJsonAsync(HttpClient client, string path, object body, string csrf) =>
        SendJsonAsync(client, HttpMethod.Put, path, body, csrf);

    private static Task<HttpResponseMessage> DeleteAsync(HttpClient client, string path, string csrf)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, path);
        request.Headers.Add("X-CSRF-Token", csrf);
        return client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> SendJsonAsync(HttpClient client, HttpMethod method, string path, object body, string csrf)
    {
        var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
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
