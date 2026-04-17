namespace OpenShock.RepositoryServer.Models.Firmware;

/// <summary>
/// Public WebSerial filter rule — either vendor-wide (omit <see cref="Pid"/>) or
/// specific (both <see cref="Vid"/> and <see cref="Pid"/> set). Used by the frontend
/// port picker, mapped to <c>navigator.serial.requestPort({ filters })</c>.
/// </summary>
public sealed record FirmwareUsbSerialFilterDto
{
    public required int Vid { get; init; }
    public int? Pid { get; init; }
}
