using System.Text.Json.Serialization;

namespace OpenShock.RepositoryServer.Models.Release;

/// <summary>
/// C# representation of the <c>release.json</c> manifest emitted by the OpenShock
/// release-tool. Mirrors the <c>ReleaseData</c> struct in
/// <c>release-tool/internal/release/build.go</c> (schema_version 1). Keep in sync with
/// the source; the release-tool is the authority for this schema.
/// </summary>
public sealed record ReleaseData
{
    [JsonPropertyName("schema_version")]
    public required int SchemaVersion { get; init; }

    [JsonPropertyName("repository")]
    public Repository? Repository { get; init; }

    [JsonPropertyName("version")]
    public required string Version { get; init; }

    [JsonPropertyName("tag")]
    public required string Tag { get; init; }

    [JsonPropertyName("prerelease")]
    public required bool Prerelease { get; init; }

    [JsonPropertyName("previous_version")]
    public string? PreviousVersion { get; init; }

    [JsonPropertyName("previous_tag")]
    public string? PreviousTag { get; init; }

    [JsonPropertyName("released_at")]
    public required DateTimeOffset ReleasedAt { get; init; }

    [JsonPropertyName("commit")]
    public required string Commit { get; init; }

    [JsonPropertyName("mandatory")]
    public required bool Mandatory { get; init; }

    [JsonPropertyName("headline")]
    public string? Headline { get; init; }

    [JsonPropertyName("changes")]
    public required List<ChangeEntry> Changes { get; init; }

    [JsonPropertyName("contributors")]
    public required List<string> Contributors { get; init; }
}
