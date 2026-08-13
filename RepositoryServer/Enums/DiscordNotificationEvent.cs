namespace OpenShock.RepositoryServer.Enums;

/// <summary>
/// Notification kinds a Discord webhook can subscribe to. Subscribing per event lets maintainer
/// alerts go somewhere other than a public releases channel.
/// </summary>
public enum DiscordNotificationEvent
{
    FirmwareReleasePublished,
    DesktopModuleVersionPublished,
    ReleaseNotesNeedEditing,
    StagedReleaseExpired
}
