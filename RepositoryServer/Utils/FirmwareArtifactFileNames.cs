using OpenShock.RepositoryServer.Enums;
using OpenShock.RepositoryServer.RepoServerDb;

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
}
