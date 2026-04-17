using OpenShock.RepositoryServer.RepoServerDb;
using OpenShock.RepositoryServer.Utils;

namespace OpenShock.RepositoryServer.Models.Firmware;

/// <summary>
/// Source traceability block for a firmware release response. Repository reference plus
/// build-specific fields, with server-constructed URLs (never stored).
/// </summary>
public sealed record FirmwareSourceDto
{
    public required RepositoryDto Repository { get; init; }
    public required string CommitHash { get; init; }
    public string? Ref { get; init; }
    public string? RunId { get; init; }
    public required string CommitUrl { get; init; }
    public string? RefUrl { get; init; }
    public string? RunUrl { get; init; }

    public static FirmwareSourceDto From(SourceRepository repository, string commitHash, string? refValue, string? runId) =>
        new()
        {
            Repository = RepositoryDto.From(repository),
            CommitHash = commitHash,
            Ref = refValue,
            RunId = runId,
            CommitUrl = SourceUrlBuilder.BuildCommitUrl(repository.Provider, repository.Owner, repository.Repo, commitHash)
                        ?? string.Empty,
            RefUrl = SourceUrlBuilder.BuildRefUrl(repository.Provider, repository.Owner, repository.Repo, refValue),
            RunUrl = SourceUrlBuilder.BuildRunUrl(repository.Provider, repository.Owner, repository.Repo, runId)
        };

    public static FirmwareSourceDto From(FirmwareVersion v) =>
        From(v.RepositoryNavigation, v.CommitHash, v.Ref, v.RunId);
}
