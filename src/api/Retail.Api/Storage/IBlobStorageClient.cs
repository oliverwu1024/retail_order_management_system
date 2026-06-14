namespace Retail.Api.Storage;

/// <summary>
/// Thin abstraction over Azure Blob storage, so services depend on "upload a blob"
/// rather than the Azure SDK directly (testable, swappable).
/// </summary>
public interface IBlobStorageClient
{
    /// <summary>
    /// Uploads <paramref name="content"/> to <paramref name="blobName"/> in
    /// <paramref name="container"/> (created with public-blob read access if absent),
    /// tagging it with <paramref name="contentType"/>. Returns the blob key (the path
    /// within the container) to persist on the entity.
    /// </summary>
    Task<string> UploadAsync(string container, string blobName, Stream content, string contentType, CancellationToken ct);
}
