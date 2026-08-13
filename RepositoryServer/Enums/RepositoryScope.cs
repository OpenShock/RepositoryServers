namespace OpenShock.RepositoryServer.Enums;

/// <summary>
/// What a registered repository is permitted to publish.
/// </summary>
/// <remarks>
/// Firmware and desktop module ingestion share one authentication scheme, so without scopes any
/// repository registered to publish desktop modules could also initialise and publish firmware
/// releases. Scopes keep an onboarding grant limited to what it was meant for.
/// </remarks>
public enum RepositoryScope
{
    PublishFirmware,
    PublishModules
}
