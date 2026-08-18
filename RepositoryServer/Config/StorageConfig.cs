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

    /// <summary>
    /// Whether to mark world-readable objects public-read with a canned ACL as they are written.
    /// </summary>
    /// <remarks>
    /// Off by default, because ACLs are not universally available and sending one where they are
    /// disabled fails the write rather than being ignored: Cloudflare R2 does not implement them at
    /// all, and an AWS bucket on the default Object Ownership of "bucket owner enforced" answers
    /// AccessControlListNotSupported.
    ///
    /// Leave it off where the bucket grants public read by policy, which is the only option on those
    /// stores. Turn it on for a bucket that has ACLs enabled and no such policy. Either way the
    /// staging prefix is never marked public: only published artifacts and module zips are.
    /// </remarks>
    public bool PublicReadAcl { get; init; }
}
