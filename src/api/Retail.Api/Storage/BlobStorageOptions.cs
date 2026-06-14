namespace Retail.Api.Storage;

/// <summary>
/// Blob storage settings bound from the <c>Storage</c> configuration section. In dev
/// the connection string is <c>UseDevelopmentStorage=true</c> (Azurite); in
/// production it comes from Key Vault / a managed identity connection.
/// </summary>
public sealed class BlobStorageOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Storage";

    /// <summary>Azure Storage connection string. Secret in production.</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Container that holds product images (DATABASE_DESIGN §3.5).</summary>
    public string ProductImagesContainer { get; set; } = "product-images";
}
