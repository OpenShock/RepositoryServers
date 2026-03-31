using System.Runtime.CompilerServices;
using System.Text.Json;
using OpenShock.RepositoryServer.Config;

namespace OpenShock.RepositoryServer.Services;

public sealed class BunnyCdnStorageService : IStorageService
{
    private readonly HttpClient _httpClient;
    private readonly string _storageUrl;

    public BunnyCdnStorageService(HttpClient httpClient, BunnyCdnStorageConfig config)
    {
        _httpClient = httpClient;
        _storageUrl = config.StorageUrl.TrimEnd('/');

        _httpClient.DefaultRequestHeaders.Add("AccessKey", config.ApiKey);
    }

    /// <inheritdoc />
    public async Task UploadFileAsync(string path, Stream content, CancellationToken cancellationToken = default)
    {
        var url = $"{_storageUrl}/{path}";

        using var streamContent = new StreamContent(content);
        streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");

        var response = await _httpClient.PutAsync(url, streamContent, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    /// <inheritdoc />
    public async Task DeleteFileAsync(string path, CancellationToken cancellationToken = default)
    {
        var url = $"{_storageUrl}/{path}";
        var response = await _httpClient.DeleteAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    /// <inheritdoc />
    public async Task DeleteDirectoryAsync(string prefix, CancellationToken cancellationToken = default)
    {
        // BunnyCDN supports deleting a directory path which removes all contents
        var url = $"{_storageUrl}/{prefix.TrimEnd('/')}/";
        var response = await _httpClient.DeleteAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<string> ListFilesAsync(string prefix = "", [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // BunnyCDN storage API: GET /{storageZone}/{path}/ returns JSON array of objects
        var url = $"{_storageUrl}/{prefix.TrimEnd('/')}/";
        var response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var entries = await JsonSerializer.DeserializeAsync<JsonElement[]>(stream, cancellationToken: cancellationToken);
        if (entries == null) yield break;

        foreach (var entry in entries)
        {
            var isDirectory = entry.GetProperty("IsDirectory").GetBoolean();
            var objectName = entry.GetProperty("ObjectName").GetString()!;
            var entryPath = string.IsNullOrEmpty(prefix) ? objectName : $"{prefix.TrimEnd('/')}/{objectName}";

            if (isDirectory)
            {
                await foreach (var file in ListFilesAsync(entryPath, cancellationToken))
                {
                    yield return file;
                }
            }
            else
            {
                yield return entryPath;
            }
        }
    }
}
