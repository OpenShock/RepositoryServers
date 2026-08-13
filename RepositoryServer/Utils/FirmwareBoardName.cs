using System.Text.RegularExpressions;

namespace OpenShock.RepositoryServer.Utils;

/// <summary>
/// Validates board names.
/// </summary>
/// <remarks>
/// A board name is a public identifier that gets interpolated into CDN paths and storage keys
/// unescaped, and hubs send it as their own board reference. Restricting it to URL-safe characters is
/// what stops a name from reshaping a storage key: notably it rejects <c>/</c> and <c>..</c>
/// sequences, which would otherwise let a board write outside its own prefix.
///
/// This lived on the request model's data annotations while administration was an HTTP API. It has to
/// live here now, because the service is the only thing left between a caller and the database.
/// </remarks>
public static partial class FirmwareBoardName
{
    public const int MaxLength = 128;

    public static bool IsValid(string? name) =>
        !string.IsNullOrEmpty(name) && name.Length <= MaxLength && Pattern().IsMatch(name);

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]*$")]
    private static partial Regex Pattern();
}
