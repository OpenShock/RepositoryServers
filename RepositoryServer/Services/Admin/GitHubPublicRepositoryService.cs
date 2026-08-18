using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace OpenShock.RepositoryServer.Services.Admin;

/// <summary>Owner and repo of a repository discovered on GitHub, in the form the allowlist stores.</summary>
public readonly record struct GitHubRepositoryRef(string Owner, string Repo)
{
    public string FullName => $"{Owner}/{Repo}";
}

/// <summary>
/// The owner the publishers page offers first, which is the organization admins log in through.
/// Registered even when there is no GitHub configuration - the development login bypass has none -
/// in which case <see cref="DefaultOwner"/> is null and the field simply starts empty.
/// </summary>
public sealed record GitHubOwnerDefaults(string? DefaultOwner);

/// <summary>
/// Lists an owner's public repositories, so registering a publisher can be a pick rather than a
/// retyping of a name that has to match GitHub exactly.
/// </summary>
/// <remarks>
/// The call is deliberately unauthenticated. Listing public repositories needs no token, so this
/// reads nothing the anonymous internet cannot already read and no GitHub credential has to be held
/// anywhere - the admin's OAuth token is still discarded at the callback
/// (<c>GitHubAuthentication.ConfigureOAuth</c> keeps <c>SaveTokens = false</c>) and the login scope
/// stays <c>read:org</c>.
///
/// The tradeoff is the anonymous rate limit, 60 requests an hour per source IP, shared by every
/// admin on this server. Hence the per-owner cache: a listing costs one request per page, and
/// repeating a lookup for an owner already seen costs none. Private repositories are absent by
/// construction, which is why the page keeps its manual owner and repo fields.
/// </remarks>
public sealed class GitHubPublicRepositoryService
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);

    // 100 is GitHub's maximum, and the cap bounds a pathological owner (or a paging bug) rather
    // than any real one: 10 pages is 1000 repositories.
    private const int PageSize = 100;
    private const int MaxPages = 10;

    private readonly HttpClient _http;
    private readonly IMemoryCache _cache;
    private readonly ILogger<GitHubPublicRepositoryService> _logger;

    public GitHubPublicRepositoryService(
        HttpClient http,
        IMemoryCache cache,
        GitHubOwnerDefaults defaults,
        ILogger<GitHubPublicRepositoryService> logger)
    {
        _http = http;
        _cache = cache;
        _logger = logger;
        DefaultOwner = defaults.DefaultOwner;
    }

    /// <summary>
    /// The owner to offer before the admin types one, so the common case - a repository in the
    /// organization that owns this server - is a pick rather than any typing at all.
    /// </summary>
    public string? DefaultOwner { get; }

    /// <summary>
    /// The owner's public repositories, sorted by name. Returns empty rather than throwing when the
    /// owner does not exist, GitHub is unreachable, or the anonymous budget is spent: this only
    /// fills a convenience picker, and the dialog it serves stays usable by typing the name.
    /// </summary>
    public async Task<GitHubRepositoryRef[]> ListPublicRepositoriesAsync(
        string owner, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(owner)) return [];

        // Keyed on the lowered name because GitHub resolves owners case-insensitively, so
        // "OpenShock" and "openshock" are one listing and should not be two cache entries.
        var key = $"{nameof(GitHubPublicRepositoryService)}:{owner.Trim().ToLowerInvariant()}";

        if (_cache.TryGetValue(key, out GitHubRepositoryRef[]? cached) && cached is not null)
        {
            return cached;
        }

        var repositories = await FetchAsync(owner.Trim(), ct);

        // Cached even when empty. A failed or rate-limited lookup that is retried on every reopen
        // of the dialog would burn the remaining anonymous budget and keep it failing.
        _cache.Set(key, repositories, CacheDuration);

        return repositories;
    }

    private async Task<GitHubRepositoryRef[]> FetchAsync(string owner, CancellationToken ct)
    {
        // Organizations and user accounts list from different endpoints and an owner name alone
        // does not say which it is, so the org endpoint is tried first and a 404 means "try the
        // user one". Only the miss costs the extra request, and only once per cache duration.
        var results = await FetchFromAsync($"orgs/{Uri.EscapeDataString(owner)}/repos", owner, ct);

        return results is null
            ? await FetchFromAsync($"users/{Uri.EscapeDataString(owner)}/repos", owner, ct) ?? []
            : results;
    }

    /// <summary>
    /// Reads one paginated listing. Null distinguishes "no such owner at this endpoint" - the only
    /// answer worth retrying elsewhere - from an empty but valid listing.
    /// </summary>
    private async Task<GitHubRepositoryRef[]?> FetchFromAsync(string path, string owner, CancellationToken ct)
    {
        var results = new List<GitHubRepositoryRef>();

        for (var page = 1; page <= MaxPages; page++)
        {
            var url = $"{path}?type=public&per_page={PageSize}&sort=full_name&page={page}";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, ct);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "Listing public repositories of {Owner} failed", owner);
                return [.. results];
            }

            using (response)
            {
                if (response.StatusCode == HttpStatusCode.NotFound && page == 1) return null;

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "Listing public repositories of {Owner} returned {Status}",
                        owner, response.StatusCode);
                    return [.. results];
                }

                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var payload = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

                if (payload.RootElement.ValueKind != JsonValueKind.Array) return [.. results];

                var count = 0;
                foreach (var element in payload.RootElement.EnumerateArray())
                {
                    count++;
                    if (TryReadRepository(element, out var repository)) results.Add(repository);
                }

                // A short page is the last page. GitHub's Link header says the same thing, but the
                // count is enough and does not need parsing.
                if (count < PageSize) break;
            }
        }

        return [.. results.OrderBy(r => r.Repo, StringComparer.OrdinalIgnoreCase)];
    }

    private static bool TryReadRepository(JsonElement element, out GitHubRepositoryRef repository)
    {
        repository = default;

        if (element.ValueKind != JsonValueKind.Object) return false;

        if (!element.TryGetProperty("name", out var nameElement)) return false;
        var name = nameElement.GetString();
        if (string.IsNullOrWhiteSpace(name)) return false;

        // The owner's login rather than the name that was typed: they differ in case whenever the
        // two are spelled differently, and the stored row should read the way GitHub writes it.
        var owner = element.TryGetProperty("owner", out var ownerElement)
                    && ownerElement.ValueKind == JsonValueKind.Object
                    && ownerElement.TryGetProperty("login", out var loginElement)
            ? loginElement.GetString()
            : null;

        if (string.IsNullOrWhiteSpace(owner)) return false;

        repository = new GitHubRepositoryRef(owner, name);
        return true;
    }
}
