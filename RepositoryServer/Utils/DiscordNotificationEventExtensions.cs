using OpenShock.RepositoryServer.Enums;

namespace OpenShock.RepositoryServer.Utils;

public static class DiscordNotificationEventExtensions
{
    public static string ToEventName(this DiscordNotificationEvent value) => value switch
    {
        DiscordNotificationEvent.FirmwareReleasePublished => "firmware_release_published",
        DiscordNotificationEvent.DesktopModuleVersionPublished => "desktop_module_version_published",
        DiscordNotificationEvent.ReleaseNotesNeedEditing => "release_notes_need_editing",
        DiscordNotificationEvent.StagedReleaseExpired => "staged_release_expired",
        _ => value.ToString().ToLowerInvariant()
    };

    public static bool TryParseEvent(string value, out DiscordNotificationEvent result)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "firmware_release_published":
                result = DiscordNotificationEvent.FirmwareReleasePublished; return true;
            case "desktop_module_version_published":
                result = DiscordNotificationEvent.DesktopModuleVersionPublished; return true;
            case "release_notes_need_editing":
                result = DiscordNotificationEvent.ReleaseNotesNeedEditing; return true;
            case "staged_release_expired":
                result = DiscordNotificationEvent.StagedReleaseExpired; return true;
            default:
                result = default; return false;
        }
    }
}
