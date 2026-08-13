using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace OpenShock.RepositoryServer.Utils;

/// <summary>
/// Recognises specific unique-index violations so controllers can map them to a meaningful conflict
/// rather than letting them escape as a 500.
/// </summary>
/// <remarks>
/// Board and chip names are unique on <c>lower(name)</c>, which is a partial/expression index created
/// in a migration rather than in the EF model. Read-then-insert guards cannot close the race on their
/// own, so the database is the real arbiter and its rejection has to be translated here.
/// </remarks>
public static class UniqueViolation
{
    public static bool IsOn(DbUpdateException ex, string constraintName) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
        && pg.ConstraintName == constraintName;

    public const string BoardNameLower = "ix_firmware_boards_name_lower";
    public const string ChipNameLower = "ix_firmware_chips_name_lower";
    public const string RepositoryIdentityLower = "ix_repositories_provider_owner_repo_lower";
}
