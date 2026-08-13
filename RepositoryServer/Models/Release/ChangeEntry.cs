using System.Text.Json.Serialization;

namespace OpenShock.RepositoryServer.Models.Release;

/// <summary>
/// A single change in a release. Mirrors the <c>ChangeEntry</c> struct in the
/// release-tool (<c>release-tool/internal/release/build.go</c>).
/// </summary>
public sealed record ChangeEntry
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("breaking")]
    public required bool Breaking { get; init; }

    [JsonPropertyName("mandatory")]
    public required bool Mandatory { get; init; }

    [JsonPropertyName("title")]
    public required string Title { get; init; }

    [JsonPropertyName("release_note")]
    public ReleaseNoteEntry? ReleaseNote { get; init; }

    [JsonPropertyName("pr")]
    public int? Pr { get; init; }

    [JsonPropertyName("notices")]
    public required List<NoticeEntry> Notices { get; init; }
}
