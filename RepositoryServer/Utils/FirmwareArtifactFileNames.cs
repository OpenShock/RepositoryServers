using OpenShock.RepositoryServer.Enums;

namespace OpenShock.RepositoryServer.Utils;

public static class FirmwareArtifactFileNames
{
    public static string GetFileName(FirmwareArtifactType type) => type switch
    {
        FirmwareArtifactType.Merged => "firmware.bin",
        FirmwareArtifactType.App => "app.bin",
        FirmwareArtifactType.Bootloader => "bootloader.bin",
        FirmwareArtifactType.Partitions => "partitions.bin",
        FirmwareArtifactType.StaticFs => "staticfs.bin",
        _ => $"{type.ToString().ToLowerInvariant()}.bin"
    };

    /// <summary>
    /// Storage path for an artifact, relative to the CDN root:
    /// <c>{version}/{boardId}/{artifactType}.bin</c>.
    /// </summary>
    /// <remarks>
    /// Deliberately keyed by the board <em>id</em>, not its name. Published versions are immutable, so
    /// a path segment has to be immutable too: renaming a board whose name was baked into storage keys
    /// would strand every artifact ever published for it, unrecoverably — nothing records the name a
    /// blob was written under. The id never changes, so a rename is a pure metadata edit.
    /// The board name remains the public identifier everywhere it is a label rather than a key:
    /// routes, response fields, ingestion and errors. See firmware-api-spec.md §4.2.
    /// </remarks>
    public static string BuildStoragePath(string version, Guid boardId, FirmwareArtifactType type)
        => $"{version}/{boardId}/{GetFileName(type)}";

    /// <summary>
    /// Absolute CDN URL for an artifact. <paramref name="cdnBase"/> must already be trimmed of any
    /// trailing slash. Consumers treat this as opaque and never reconstruct it.
    /// </summary>
    public static string BuildUrl(string cdnBase, string version, Guid boardId, FirmwareArtifactType type)
        => $"{cdnBase}/{BuildStoragePath(version, boardId, type)}";

    /// <summary>Storage prefix holding every artifact staged for one in-progress release.</summary>
    public const string StagingRoot = "_staging";

    /// <summary>All staged artifacts for a release, so abort and TTL cleanup can drop them wholesale.</summary>
    public static string BuildStagingPrefix(Guid releaseId) => $"{StagingRoot}/{releaseId}";

    /// <summary>
    /// Staging path for an artifact: <c>_staging/{releaseId}/{boardId}/{artifactType}.bin</c>.
    /// </summary>
    /// <remarks>
    /// Uploads land here rather than at the published key, and are promoted by
    /// <c>PublishRelease</c>. Keeping in-progress bytes off the public path is what makes a first
    /// publish atomic: a published key is written exactly once, and an aborted or expired release can
    /// be deleted wholesale without any chance of removing something a live version is serving.
    /// Keyed by release id, so two releases for the same version can never collide.
    /// </remarks>
    public static string BuildStagingPath(Guid releaseId, Guid boardId, FirmwareArtifactType type)
        => $"{BuildStagingPrefix(releaseId)}/{boardId}/{GetFileName(type)}";
}
