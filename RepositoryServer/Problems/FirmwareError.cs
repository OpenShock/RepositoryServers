using System.Net;

namespace OpenShock.RepositoryServer.Problems;

public static class FirmwareError
{
    public static OpenShockProblem FirmwareVersionNotFound => new("Firmware.VersionNotFound", "The referenced firmware version was not found", HttpStatusCode.NotFound);
    public static OpenShockProblem FirmwareBoardNotFound => new("Firmware.BoardNotFound", "The referenced firmware board was not found", HttpStatusCode.NotFound);
    public static OpenShockProblem FirmwareChipNotFound => new("Firmware.ChipNotFound", "The referenced firmware chip was not found", HttpStatusCode.NotFound);
    public static OpenShockProblem FirmwareInvalidChannel => new("Firmware.InvalidChannel", "The channel provided is not valid");
    public static OpenShockProblem FirmwareInvalidSemver => new("Firmware.InvalidSemver", "The version provided is not a valid Semantic Versioning string");
    public static OpenShockProblem FirmwareInvalidArtifactType => new("Firmware.InvalidArtifactType", "The artifact type provided is not valid");
    public static OpenShockProblem FirmwareInvalidReleaseNoteType => new("Firmware.InvalidReleaseNoteType", "The release note type provided is not valid");
    public static OpenShockProblem FirmwareInvalidArchitecture => new("Firmware.InvalidArchitecture", "The architecture provided is not valid");
    public static OpenShockProblem FirmwareBoardInUse => new("Firmware.BoardInUse", "Cannot delete board that has associated artifacts", HttpStatusCode.Conflict);
    public static OpenShockProblem FirmwareChipInUse => new("Firmware.ChipInUse", "Cannot delete chip that has associated boards", HttpStatusCode.Conflict);
    public static OpenShockProblem FirmwareMissingRequiredArtifacts(string boardId, IEnumerable<string> missing) =>
        new("Firmware.MissingRequiredArtifacts", $"Board '{boardId}' is missing required artifact types: {string.Join(", ", missing)}");
    public static OpenShockProblem FirmwareArtifactNotFound => new("Firmware.ArtifactNotFound", "The referenced firmware artifact was not found", HttpStatusCode.NotFound);

    public static OpenShockProblem FirmwareReleaseNotFound => new("Firmware.ReleaseNotFound", "The referenced firmware release was not found", HttpStatusCode.NotFound);
    public static OpenShockProblem FirmwareReleaseNotStaging => new("Firmware.ReleaseNotStaging", "The firmware release is not in staging status", HttpStatusCode.Conflict);
    public static OpenShockProblem FirmwareReleaseAlreadyStaging => new("Firmware.ReleaseAlreadyStaging", "A staging release for this version already exists", HttpStatusCode.Conflict);
    public static OpenShockProblem FirmwareReleaseBoardsEmpty => new("Firmware.ReleaseBoardsEmpty", "At least one board must be declared for a release");
    public static OpenShockProblem FirmwareBoardNotDeclared => new("Firmware.BoardNotDeclared", "The board was not declared in the release init");
    public static OpenShockProblem FirmwareReleaseIncomplete(IEnumerable<string> missingBoards) =>
        new("Firmware.ReleaseIncomplete", $"Not all declared boards have been uploaded. Missing: {string.Join(", ", missingBoards)}");
}
