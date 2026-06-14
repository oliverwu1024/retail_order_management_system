using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Options;

namespace Retail.Api.Storage;

/// <summary>
/// Azure Blob (Azurite in dev) implementation of <see cref="IBlobStorageClient"/>.
/// </summary>
/// <remarks>
/// The <see cref="BlobServiceClient"/> is built lazily INSIDE <see cref="UploadAsync"/>
/// from the configured connection string — never in the constructor — so that
/// resolving this (singleton) service for an unrelated catalogue request never
/// touches the connection string. A blank/invalid connection string therefore only
/// fails an actual upload, not every catalogue read. Uploads are an infrequent admin
/// action, so building the client per call is fine.
/// </remarks>
public sealed class BlobStorageClient : IBlobStorageClient
{
    private readonly BlobStorageOptions _options;

    public BlobStorageClient(IOptions<BlobStorageOptions> options)
    {
        _options = options.Value;
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
        var serviceClient = new BlobServiceClient(_options.ConnectionString, ClientOptions);
        BlobContainerClient containerClient = serviceClient.GetBlobContainerClient(container);

        // Public-blob access so the stored image is directly URL-addressable by the
        // storefront (product images are public). No-op if the container exists.
        await containerClient.CreateIfNotExistsAsync(PublicAccessType.Blob, cancellationToken: ct);

        BlobClient blob = containerClient.GetBlobClient(blobName);
        await blob.UploadAsync(
            content,
            new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } },
            ct);

        return blobName;
    }
}
