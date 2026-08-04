using System.Text.Json.Serialization;

namespace OpenShock.RepositoryServer.Models.Release;

/// <summary>
/// Source repository reference for a release. Mirrors the <c>Repository</c> struct in
/// the release-tool (<c>release-tool/internal/release/build.go</c>).
/// </summary>
public sealed record Repository
{
    [JsonPropertyName("platform")]
    public required string Platform { get; init; }

    [JsonPropertyName("owner")]
    public required string Owner { get; init; }

    [JsonPropertyName("repo")]
    public required string Repo { get; init; }
}
