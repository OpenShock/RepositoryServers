using System.Text.Json.Serialization;
using OpenShock.RepositoryServer.Utils;

namespace OpenShock.RepositoryServer.Models;

public sealed class CreateModuleVersionRequest
{
    public required Uri ZipUrl { get; init; }
    [JsonConverter(typeof(ByteArrayHexConverter))]
    public required byte[] Sha256Hash { get; init; }
    public Uri? ChangelogUrl { get; init; } = null;
    public Uri? ReleaseUrl { get; init; } = null;
}