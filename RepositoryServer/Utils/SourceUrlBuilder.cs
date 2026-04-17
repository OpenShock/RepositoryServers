using OpenShock.RepositoryServer.Enums;

namespace OpenShock.RepositoryServer.Utils;

/// <summary>
/// Builds human-browsable URLs for source-traceability fields on firmware release
/// responses. Pure function, provider-switched. Unknown providers return <c>null</c>
/// so consumers can degrade gracefully per firmware-api-spec.md §10 ("Consumers should
/// handle unknown providers gracefully"). Today only GitHub is a known provider.
/// </summary>
public static class SourceUrlBuilder
{
    public static string? BuildCommitUrl(RepositoryProvider provider, string owner, string repo, string commitHash)
    {
        if (string.IsNullOrWhiteSpace(commitHash)) return null;

        return provider switch
        {
            RepositoryProvider.Github => $"https://github.com/{owner}/{repo}/commit/{commitHash}",
            _ => null
        };
    }

    /// <summary>
    /// Builds a URL for a git ref. Handles <c>refs/tags/NAME</c>, <c>refs/heads/NAME</c>,
    /// and bare refs. Returns null when <paramref name="refValue"/> is null/empty or for
    /// unrecognized providers.
    /// </summary>
    public static string? BuildRefUrl(RepositoryProvider provider, string owner, string repo, string? refValue)
    {
        if (string.IsNullOrWhiteSpace(refValue)) return null;

        return provider switch
        {
            RepositoryProvider.Github => BuildGitHubRefUrl(owner, repo, refValue),
            _ => null
        };
    }

    public static string? BuildRunUrl(RepositoryProvider provider, string owner, string repo, string? runId)
    {
        if (string.IsNullOrWhiteSpace(runId)) return null;

        return provider switch
        {
            RepositoryProvider.Github => $"https://github.com/{owner}/{repo}/actions/runs/{runId}",
            _ => null
        };
    }

    private static string BuildGitHubRefUrl(string owner, string repo, string refValue)
    {
        const string tagPrefix = "refs/tags/";
        const string branchPrefix = "refs/heads/";

        if (refValue.StartsWith(tagPrefix, StringComparison.Ordinal))
        {
            var tag = refValue[tagPrefix.Length..];
            return $"https://github.com/{owner}/{repo}/releases/tag/{tag}";
        }

        if (refValue.StartsWith(branchPrefix, StringComparison.Ordinal))
        {
            var branch = refValue[branchPrefix.Length..];
            return $"https://github.com/{owner}/{repo}/tree/{branch}";
        }

        // Bare ref — best effort: treat as a branch name.
        return $"https://github.com/{owner}/{repo}/tree/{refValue}";
    }
}
