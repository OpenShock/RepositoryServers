namespace OpenShock.RepositoryServer.Utils;

public static class DiscordWebhookMasking
{
    /// <summary>
    /// Renders a webhook URL safe to return from the API.
    /// </summary>
    /// <remarks>
    /// A Discord webhook URL is <c>https://discord.com/api/webhooks/{id}/{token}</c>, where the token
    /// is the whole credential. The id is retained because it is what identifies the webhook in
    /// Discord's own UI, which is what an operator needs to match a row against a channel.
    /// </remarks>
    public static string Mask(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return string.Empty;

        var trimmed = url.TrimEnd('/');
        var lastSlash = trimmed.LastIndexOf('/');

        // No path to speak of — reveal nothing rather than guessing at the shape.
        if (lastSlash <= 0) return "***";

        return $"{trimmed[..(lastSlash + 1)]}***";
    }
}
