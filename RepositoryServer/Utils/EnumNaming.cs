using System.Text.Json;
using OpenShock.RepositoryServer.Enums;

namespace OpenShock.RepositoryServer.Utils;

/// <summary>
/// Shared enum-to-string formatting used on API responses so client-facing values match
/// the Postgres enum values (snake_case_lower) for the enums where the C# member names
/// diverge from the wire format.
/// </summary>
public static class EnumNaming
{
    public static string? FormatArchitecture(FirmwareChipArchitecture? architecture)
    {
        if (architecture is null) return null;
        // RiscV → risc_v, Xtensa → xtensa
        return JsonNamingPolicy.SnakeCaseLower.ConvertName(architecture.Value.ToString());
    }
}
