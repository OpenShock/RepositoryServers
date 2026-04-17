using OpenShock.RepositoryServer.Enums;
using OpenShock.RepositoryServer.Models.Firmware;
using OpenShock.RepositoryServer.RepoServerDb;

namespace OpenShock.RepositoryServer.Utils;

/// <summary>
/// Shared projection helpers for firmware read endpoints (latest, versions, single-board).
/// </summary>
public static class FirmwareResponseMapper
{
    public static FirmwareArtifactDto ToArtifactDto(FirmwareArtifact artifact, string version, string cdnBase) => new()
    {
        Type = artifact.ArtifactType.ToString().ToLowerInvariant(),
        Url = $"{cdnBase}/{version}/{artifact.BoardId}/{FirmwareArtifactFileNames.GetFileName(artifact.ArtifactType)}",
        Sha256Hash = Convert.ToHexString(artifact.HashSha256),
        FileSize = artifact.FileSize
    };

    public static FirmwareReleaseDto ToReleaseDto(FirmwareVersion version, IEnumerable<FirmwareBoard> boardsById, string cdnBase)
    {
        var boardLookup = boardsById.ToDictionary(b => b.Id);

        var boardMap = new Dictionary<string, FirmwareBoardDetailDto>();
        foreach (var artifact in version.Artifacts)
        {
            if (!boardLookup.TryGetValue(artifact.BoardId, out var board))
                continue;

            if (!boardMap.TryGetValue(artifact.BoardId, out var detail))
            {
                detail = new FirmwareBoardDetailDto
                {
                    Chip = new FirmwareChipRefDto
                    {
                        Id = board.ChipNavigation.Id,
                        Name = board.ChipNavigation.Name
                    },
                    Discontinued = board.Discontinued,
                    Artifacts = new List<FirmwareArtifactDto>()
                };
                boardMap[artifact.BoardId] = detail;
            }

            detail.Artifacts.Add(ToArtifactDto(artifact, version.Version, cdnBase));
        }

        var releaseNotes = version.ReleaseNotes
            .OrderBy(n => n.Index)
            .Select(n => new FirmwareReleaseNoteDto
            {
                Type = n.SectionType.ToString().ToLowerInvariant(),
                Title = n.Title,
                Content = n.Content
            })
            .ToList();

        return new FirmwareReleaseDto
        {
            Version = version.Version,
            Channel = version.Channel.ToString().ToLowerInvariant(),
            ReleaseDate = version.ReleaseDate,
            Source = FirmwareSourceDto.From(version),
            ReleaseNotes = releaseNotes,
            Boards = boardMap
        };
    }
}
