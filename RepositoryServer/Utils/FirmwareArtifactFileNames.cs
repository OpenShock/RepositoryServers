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
}
