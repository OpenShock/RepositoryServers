namespace OpenShock.RepositoryServer.Enums;

/// <summary>
/// Severity level for a firmware advisory surfaced on the manifest endpoint.
/// Lowercase member names so the Postgres enum values match the public API contract
/// (<c>critical</c>, <c>warning</c>, <c>info</c>).
/// </summary>
public enum AdvisorySeverity
{
    Critical,
    Warning,
    Info
}
