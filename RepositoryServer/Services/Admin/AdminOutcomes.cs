using OpenShock.RepositoryServer.Enums;
using OpenShock.RepositoryServer.Utils;

namespace OpenShock.RepositoryServer.Services.Admin;

/// <summary>A record with this name already exists. Names are compared case-insensitively.</summary>
public readonly record struct NameConflict(string Name);

/// <summary>
/// The name is not usable as an identifier. Board names end up in URLs and storage keys, so the
/// character set is restricted rather than escaped at every use.
/// </summary>
public readonly record struct InvalidName(string Name);

/// <summary>
/// The target exists but is referenced by something that would be orphaned by removing it.
/// </summary>
/// <param name="ReferencedBy">What holds the reference, for a message that names the obstacle.</param>
public readonly record struct InUse(string ReferencedBy);

/// <summary>
/// Something the operation referred to does not exist. Distinct from <c>NotFound</c>, which means the
/// target of the operation itself is missing.
/// </summary>
/// <param name="Reference">Which reference was unresolvable, e.g. "chip" or "usb device".</param>
public readonly record struct ReferenceNotFound(string Reference);

/// <summary>A release cannot be edited in its current status.</summary>
public readonly record struct NotEditable(ReleaseStatus Status);

/// <summary>The submitted changelog could not be parsed into release notes.</summary>
public readonly record struct InvalidChangelog(ChangelogParseError Error);
