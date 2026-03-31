using System.ComponentModel.DataAnnotations;

namespace OpenShock.RepositoryServer.Config;

public sealed class StorageConfig
{
    [Required] public required StorageType Type { get; init; }

    public BunnyCdnStorageConfig? BunnyCdn { get; init; }
    public LocalStorageConfig? Local { get; init; }
    public S3StorageConfig? S3 { get; init; }
}

public enum StorageType
{
    BunnyCdn,
    Local,
    S3,
}

public sealed class BunnyCdnStorageConfig
{
    [Required(AllowEmptyStrings = false)] public required string StorageUrl { get; init; }
    [Required(AllowEmptyStrings = false)] public required string ApiKey { get; init; }
}

public sealed class LocalStorageConfig
{
    [Required(AllowEmptyStrings = false)] public required string BasePath { get; init; }
}

public sealed class S3StorageConfig
{
    [Required(AllowEmptyStrings = false)] public required string BucketName { get; init; }
    [Required(AllowEmptyStrings = false)] public required string AccessKey { get; init; }
    [Required(AllowEmptyStrings = false)] public required string SecretKey { get; init; }

    /// <summary>
    /// Custom endpoint URL for S3-compatible services (Cloudflare R2, MinIO, etc.).
    /// Leave null for AWS S3.
    /// </summary>
    public string? ServiceUrl { get; init; }

    /// <summary>
    /// Optional key prefix prepended to all paths, e.g. "firmware/".
    /// </summary>
    public string? KeyPrefix { get; init; }

    /// <summary>
    /// AWS region. Required for AWS S3, optional for most S3-compatible services.
    /// </summary>
    public string? Region { get; init; }
}
