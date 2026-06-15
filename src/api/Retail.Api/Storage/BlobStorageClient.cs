using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Options;

namespace Retail.Api.Storage;

/// <summary>
/// Azure Blob (Azurite in dev) implementation of <see cref="IBlobStorageClient"/>.
/// </summary>
/// <remarks>
/// The <see cref="BlobServiceClient"/> is built once via <see cref="Lazy{T}"/> on first
/// use — never in the constructor — so that resolving this (singleton) service for an
/// unrelated catalogue request never touches the connection string (a blank/invalid one
/// only fails an actual blob op, not every catalogue read). Lazy keeps that property
/// while still reusing one client + its pooled connection pipeline (per Azure SDK guidance).
/// </remarks>
public sealed class BlobStorageClient : IBlobStorageClient
{
    private readonly BlobStorageOptions _options;
    private readonly Lazy<BlobServiceClient> _serviceClient;

    public BlobStorageClient(IOptions<BlobStorageOptions> options)
    {
        _options = options.Value;
        _serviceClient = new Lazy<BlobServiceClient>(
            () => new BlobServiceClient(_options.ConnectionString, ClientOptions));
    }

    // Pin the storage service API version: the SDK defaults to the newest version
    // (2026-06-06), which Azurite (dev/test) doesn't yet recognise and rejects with
    // 400. A pinned, widely-supported version works against both Azurite AND real
    // Azure (basic blob ops are version-agnostic).
    private static readonly BlobClientOptions ClientOptions =
        new(BlobClientOptions.ServiceVersion.V2025_07_05);

    /// <inheritdoc />
    public async Task<string> UploadAsync(string container, string blobName, Stream content, string contentType, CancellationToken ct)
    {
        BlobContainerClient containerClient = _serviceClient.Value.GetBlobContainerClient(container);

        // Public-read only when configured (dev/storefront). Default is private (None) so
        // production isn't anonymously readable unless explicitly opted in. No-op if exists.
        PublicAccessType access = _options.PublicReadImages ? PublicAccessType.Blob : PublicAccessType.None;
        await containerClient.CreateIfNotExistsAsync(access, cancellationToken: ct);

        BlobClient blob = containerClient.GetBlobClient(blobName);
        await blob.UploadAsync(
            content,
            new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } },
            ct);

        return blobName;
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string container, string blobName, CancellationToken ct)
    {
        BlobContainerClient containerClient = _serviceClient.Value.GetBlobContainerClient(container);
        await containerClient.DeleteBlobIfExistsAsync(blobName, cancellationToken: ct);
    }
}
