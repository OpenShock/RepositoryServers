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
    public static OpenShockProblem FirmwareInvalidArchitecture => new("Firmware.InvalidArchitecture", "The architecture provided is not valid");
    public static OpenShockProblem FirmwareBoardInUse => new("Firmware.BoardInUse", "Cannot delete board that has associated artifacts", HttpStatusCode.Conflict);
    public static OpenShockProblem FirmwareChipInUse => new("Firmware.ChipInUse", "Cannot delete chip that has associated boards", HttpStatusCode.Conflict);
    public static OpenShockProblem FirmwareMissingRequiredArtifacts(string boardId, IEnumerable<string> missing) =>
        new("Firmware.MissingRequiredArtifacts", $"Board '{boardId}' is missing required artifact types: {string.Join(", ", missing)}");
    public static OpenShockProblem FirmwareArtifactNotFound => new("Firmware.ArtifactNotFound", "The referenced firmware artifact was not found", HttpStatusCode.NotFound);

    public static OpenShockProblem FirmwareReleaseNotFound => new("Firmware.ReleaseNotFound", "The referenced firmware release was not found", HttpStatusCode.NotFound);
    public static OpenShockProblem FirmwareReleaseNotEditable => new("Firmware.ReleaseNotEditable", "The firmware release is not in a staging or editing status", HttpStatusCode.Conflict);
    public static OpenShockProblem FirmwareReleaseNotStaging => new("Firmware.ReleaseNotStaging", "The firmware release is not in staging status", HttpStatusCode.Conflict);
    public static OpenShockProblem FirmwareReleaseAlreadyStaging => new("Firmware.ReleaseAlreadyStaging", "A staging release for this version already exists", HttpStatusCode.Conflict);
    public static OpenShockProblem FirmwareReleaseBoardsEmpty => new("Firmware.ReleaseBoardsEmpty", "At least one board must be declared for a release");
    public static OpenShockProblem FirmwareBoardNotDeclared => new("Firmware.BoardNotDeclared", "The board was not declared in the release init");
    public static OpenShockProblem FirmwareReleaseIncomplete(IEnumerable<string> missingBoards) =>
        new("Firmware.ReleaseIncomplete", $"Not all declared boards have been uploaded. Missing: {string.Join(", ", missingBoards)}");

    public static OpenShockProblem FirmwareInvalidChangelog(string reason) =>
        new("firmware/invalid-changelog", "The supplied changelog markdown is not valid", HttpStatusCode.BadRequest, reason);
    public static OpenShockProblem FirmwareSha256Mismatch(string detail) =>
        new("firmware/sha256-mismatch", "Uploaded artifact hash does not match the provided SHA-256 manifest", HttpStatusCode.BadRequest, detail);
    public static OpenShockProblem FirmwareManifestKeysMismatch(string detail) =>
        new("firmware/manifest-keys-mismatch", "The SHA-256 manifest keys do not match the uploaded files", HttpStatusCode.BadRequest, detail);
    public static OpenShockProblem FirmwareReleaseNotesNotFinalized =>
        new("firmware/release-notes-not-finalized", "Release notes must be finalized before publish", HttpStatusCode.Conflict);

    public static OpenShockProblem FirmwareRepositoryNotFound => new("Firmware.RepositoryNotFound", "The referenced source repository was not found", HttpStatusCode.NotFound);
    public static OpenShockProblem FirmwareRepositoryInUse => new("Firmware.RepositoryInUse", "Cannot delete repository that is referenced by firmware or desktop versions", HttpStatusCode.Conflict);

    public static OpenShockProblem FirmwareUsbDeviceNotFound => new("Firmware.UsbDeviceNotFound", "The referenced USB device was not found", HttpStatusCode.NotFound);
    public static OpenShockProblem FirmwareUsbDeviceInUse => new("Firmware.UsbDeviceInUse", "Cannot delete USB device that is still linked to a chip or board", HttpStatusCode.Conflict);
    public static OpenShockProblem FirmwareUsbSerialFilterNotFound => new("Firmware.UsbSerialFilterNotFound", "The referenced USB serial filter was not found", HttpStatusCode.NotFound);
}
