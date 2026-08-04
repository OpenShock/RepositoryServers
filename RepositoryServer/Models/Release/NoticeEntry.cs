using System.Text.Json.Serialization;

namespace OpenShock.RepositoryServer.Models.Release;

/// <summary>
/// A leveled notice attached to a change. Mirrors the <c>NoticeEntry</c> struct in the
/// release-tool (<c>release-tool/internal/release/build.go</c>).
/// </summary>
public sealed record NoticeEntry
{
    [JsonPropertyName("level")]
    public required string Level { get; init; }

    [JsonPropertyName("message")]
    public required string Message { get; init; }
}
