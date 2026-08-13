using System.Text.Json.Serialization;

namespace OpenShock.RepositoryServer.Models.Release;

/// <summary>
/// End-user-facing release note for a change. Mirrors the <c>ReleaseNoteEntry</c> struct
/// in the release-tool (<c>release-tool/internal/release/build.go</c>).
/// </summary>
public sealed record ReleaseNoteEntry
{
    [JsonPropertyName("title")]
    public required string Title { get; init; }

    [JsonPropertyName("description")]
    public List<string>? Description { get; init; }
}
