using OpenShock.RepositoryServer.Config;

namespace OpenShock.RepositoryServer.Services;

public sealed class CdnStorageService
{
    private readonly HttpClient _httpClient;
    private readonly string _storageUrl;

    public CdnStorageService(HttpClient httpClient, ApiConfig config)
    {
        _httpClient = httpClient;
        _storageUrl = config.Firmware.CdnStorageUrl.TrimEnd('/');

        _httpClient.DefaultRequestHeaders.Add("AccessKey", config.Firmware.CdnStorageApiKey);
    }

    /// <summary>
    /// Uploads a file to BunnyCDN storage.
    /// </summary>
    /// <param name="path">Path relative to storage zone root, e.g. "1.0.0/board-name/app.bin"</param>
    /// <param name="content">File content stream</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task UploadFileAsync(string path, Stream content, CancellationToken cancellationToken = default)
    {
        var url = $"{_storageUrl}/{path}";

        using var streamContent = new StreamContent(content);
        streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");

        var response = await _httpClient.PutAsync(url, streamContent, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
