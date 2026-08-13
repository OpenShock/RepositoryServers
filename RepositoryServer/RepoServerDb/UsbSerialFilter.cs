namespace OpenShock.RepositoryServer.RepoServerDb;

/// <summary>
/// WebSerial filter rule. Either a vendor-wide match (null <see cref="Pid"/>) or a specific
/// device match (both fields set). Consumed by the frontend port picker via
/// <c>navigator.serial.requestPort({ filters })</c>.
/// Unique constraint on <c>(vid, pid)</c> with PostgreSQL <c>NULLS NOT DISTINCT</c>
/// (declared as raw SQL in the migration — EF Core has no fluent shortcut for it).
/// </summary>
public class UsbSerialFilter
{
    public Guid Id { get; set; }
    public int Vid { get; set; }
    public int? Pid { get; set; }
    public string? Description { get; set; }
}
