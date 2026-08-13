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
    /// <c>{version}/{boardName}/{artifactType}.bin</c>.
    /// </summary>
    /// <remarks>
    /// The board <em>name</em> is the path segment, not the board id — see firmware-api-spec.md §4.2.
    /// Board names are validated as URL-safe on create/update, so they need no escaping here.
    /// </remarks>
    public static string BuildStoragePath(string version, string boardName, FirmwareArtifactType type)
        => $"{version}/{boardName}/{GetFileName(type)}";

    /// <summary>
    /// Absolute CDN URL for an artifact. <paramref name="cdnBase"/> must already be trimmed of any
    /// trailing slash.
    /// </summary>
    public static string BuildUrl(string cdnBase, string version, string boardName, FirmwareArtifactType type)
        => $"{cdnBase}/{BuildStoragePath(version, boardName, type)}";
}
