using System.Net;

namespace OpenShock.RepositoryServer.Problems;

public static class DiscordError
{
    public static OpenShockProblem DiscordWebhookNotFound =>
        new("Discord.WebhookNotFound", "The referenced Discord webhook was not found", HttpStatusCode.NotFound);

    public static OpenShockProblem DiscordInvalidNotificationEvent(string value) =>
        new("Discord.InvalidNotificationEvent",
            $"Unknown notification event '{value}'. Valid events: firmware_release_published, " +
            "desktop_module_version_published, release_notes_need_editing, staged_release_expired");
}
