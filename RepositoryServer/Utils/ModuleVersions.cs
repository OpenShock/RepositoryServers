using Semver;
using Version = OpenShock.RepositoryServer.RepoServerDb.Models.Version;

namespace OpenShock.RepositoryServer.Utils;

/// <summary>
/// Ordering for desktop module versions.
/// </summary>
/// <remarks>
/// Module versions are stored as text, so ordering them the way the database does puts 10.0.0 under
/// 9.0.0 and pre-releases above the release they precede. Both matter to a reader deciding what is
/// current, so the admin UI sorts them as semver instead.
///
/// A row that does not parse is kept rather than dropped: publishing validates the version, but a row
/// written before that validation existed, or by hand, still has to be visible to be withdrawn.
/// </remarks>
public static class ModuleVersions
{
    /// <summary>The parsed version, or null when the stored string is not strict semver.</summary>
    public static SemVersion? Parse(string versionName) =>
        SemVersion.TryParse(versionName, SemVersionStyles.Strict, out var parsed, maxLength: 64)
            ? parsed
            : null;

    /// <summary>Newest first, with anything unparseable last and in name order among itself.</summary>
    public static IEnumerable<Version> NewestFirst(IEnumerable<Version> versions)
    {
        var rows = versions
            .Select(version => (Version: version, Parsed: Parse(version.VersionName)))
            .ToArray();

        var ordered = rows
            .Where(row => row.Parsed is not null)
            .OrderByDescending(row => row.Parsed!, SemVersion.SortOrderComparer)
            .Select(row => row.Version);

        var unparseable = rows
            .Where(row => row.Parsed is null)
            .Select(row => row.Version)
            .OrderBy(version => version.VersionName, StringComparer.Ordinal);

        return ordered.Concat(unparseable);
    }

    /// <summary>The highest version, or null when there are none that parse.</summary>
    public static string? Latest(IEnumerable<Version> versions) =>
        NewestFirst(versions).FirstOrDefault(version => Parse(version.VersionName) is not null)
            ?.VersionName;
}
