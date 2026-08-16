using OpenShock.RepositoryServer.Config;
using OpenShock.RepositoryServer.Enums;
using OpenShock.RepositoryServer.Utils;
using System.Net;

namespace OpenShock.RepositoryServer.Services;

/// <param name="Url">The URL that was probed, as it would be advertised to a hub.</param>
/// <param name="Status">HTTP status observed, or null if the request never got one.</param>
/// <param name="Detail">Why it failed, for the problem response and the log.</param>
public sealed record UnreachableArtifact(string Url, HttpStatusCode? Status, string Detail);

/// <summary>
/// Checks that published artifacts are actually retrievable at the URLs the server hands out.
/// </summary>
/// <remarks>
/// Writing to storage and advertising a URL are two independent facts here: the server uploads
/// through <see cref="IStorageService"/> and builds URLs from <c>Firmware:CdnBaseUrl</c>, and
/// nothing has ever tied the two together. An instance whose storage is not the storage behind that
/// hostname — a Local backend on a deployment that advertises a CDN, or a bucket the CDN is not
/// fronting — uploads every artifact successfully, publishes successfully, and serves a version
/// whose every download is a 404.
///
/// That state is invisible from the API. The version lists, the artifact hashes are correct, the
/// sizes are right; only a client actually fetching the bytes finds out, and the clients here are
/// hubs applying an OTA update. So the check belongs at publish, where it can still refuse.
/// </remarks>
public sealed class PublishedArtifactVerifier
{
    private readonly HttpClient _http;
    private readonly ApiConfig _config;
    private readonly ILogger<PublishedArtifactVerifier> _logger;

    public PublishedArtifactVerifier(
        HttpClient http, ApiConfig config, ILogger<PublishedArtifactVerifier> logger)
    {
        _http = http;
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Probes every artifact of a release. Returns the ones that could not be retrieved; an empty
    /// list means every artifact answered 200.
    /// </summary>
    /// <remarks>
    /// Probes run concurrently — a release is up to five artifacts per board across a dozen boards,
    /// and doing that serially would add real latency to every publish.
    ///
    /// A freshly written object can take a moment to become visible at the edge, so a miss is
    /// retried a few times before it counts. The retry is deliberately short: it exists to absorb
    /// propagation, not to wait out a misconfiguration, which will never resolve no matter how long
    /// the publish blocks.
    /// </remarks>
    public async Task<IReadOnlyList<UnreachableArtifact>> FindUnreachableAsync(
        string version,
        IEnumerable<(Guid BoardId, FirmwareArtifactType Type)> artifacts,
        CancellationToken ct = default)
    {
        if (!_config.Firmware.VerifyPublishedArtifacts)
        {
            return [];
        }

        var cdnBase = _config.Firmware.CdnBaseUrl.TrimEnd('/');

        var probes = artifacts
            .Select(a => FirmwareArtifactFileNames.BuildUrl(cdnBase, version, a.BoardId, a.Type))
            .Distinct()
            .Select(url => ProbeAsync(url, ct));

        var results = await Task.WhenAll(probes);

        return results.Where(r => r is not null).Select(r => r!).ToList();
    }

    private async Task<UnreachableArtifact?> ProbeAsync(string url, CancellationToken ct)
    {
        const int attempts = 3;
        UnreachableArtifact? last = null;

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            if (attempt > 1)
            {
                await Task.Delay(TimeSpan.FromSeconds(2 * (attempt - 1)), ct);
            }

            try
            {
                // HEAD, because the body is not wanted and some of these are megabytes. A store that
                // refuses HEAD reports 405, which is treated as reachable below rather than as a
                // failed publish - the object is evidently there, the method is not supported.
                using var request = new HttpRequestMessage(HttpMethod.Head, url);
                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

                if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.MethodNotAllowed)
                {
                    return null;
                }

                last = new UnreachableArtifact(url, response.StatusCode, DescribeStatus(response.StatusCode));
            }
            catch (HttpRequestException ex)
            {
                last = new UnreachableArtifact(url, null, $"request failed: {ex.Message}");
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                last = new UnreachableArtifact(url, null, "timed out");
            }
        }

        _logger.LogError(
            "Published artifact is not retrievable at {Url}: {Detail}", last!.Url, last.Detail);

        return last;
    }

    private static string DescribeStatus(HttpStatusCode status) => status switch
    {
        HttpStatusCode.NotFound =>
            "not found - the storage this server writes to is not the storage behind Firmware:CdnBaseUrl",
        HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized =>
            "access denied - the artifact exists but is not publicly readable",
        _ => $"unexpected status {(int)status}"
    };
}
