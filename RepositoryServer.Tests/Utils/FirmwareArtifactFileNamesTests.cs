using OpenShock.RepositoryServer.Enums;
using OpenShock.RepositoryServer.Utils;

namespace OpenShock.RepositoryServer.Tests.Utils;

public class FirmwareArtifactFileNamesTests
{
    [Test]
    [Arguments(FirmwareArtifactType.Merged, "firmware.bin")]
    [Arguments(FirmwareArtifactType.App, "app.bin")]
    [Arguments(FirmwareArtifactType.Bootloader, "bootloader.bin")]
    [Arguments(FirmwareArtifactType.Partitions, "partitions.bin")]
    [Arguments(FirmwareArtifactType.StaticFs, "staticfs.bin")]
    public async Task GetFileName_KnownType_ReturnsSpecFilename(FirmwareArtifactType type, string expected)
    {
        var actual = FirmwareArtifactFileNames.GetFileName(type);
        await Assert.That(actual).IsEqualTo(expected);
    }

    [Test]
    public async Task GetFileName_Covers_AllEnumValues()
    {
        foreach (var value in Enum.GetValues<FirmwareArtifactType>())
        {
            var result = FirmwareArtifactFileNames.GetFileName(value);
            await Assert.That(result).IsNotNull();
            await Assert.That(result.EndsWith(".bin", StringComparison.Ordinal)).IsTrue();
        }
    }
}
