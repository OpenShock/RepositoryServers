using System.Diagnostics.CodeAnalysis;
using OpenShock.RepositoryServer.RepoServerDb.Models;

namespace OpenShock.RepositoryServer.Components.Shared;

/// <summary>
/// A parsed module form, ready for the service. The id is passed through as typed — the service
/// lowercases it — so the field does not rewrite itself under the user.
/// </summary>
public sealed record ModuleFormValues(
    string Id, string Name, string Description, Uri? SourceUrl, Uri? IconUrl, Guid? RepositoryId);

/// <summary>
/// Bound state for <see cref="ModuleFormDialog"/>.
/// </summary>
/// <remarks>
/// A model object rather than a field per input on the page, because two pages open this form and
/// both would otherwise carry the same six fields. It also removes the ordering hazard the pages had
/// to comment around: a save captures the model instance, so clearing the form for the next open
/// means replacing the instance, and the change already in flight keeps the values it was given.
/// </remarks>
public sealed class ModuleFormModel
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? SourceUrl { get; set; }
    public string? IconUrl { get; set; }
    public Guid? RepositoryId { get; set; }

    public static ModuleFormModel From(Module module) => new()
    {
        Id = module.Id,
        Name = module.Name,
        Description = module.Description,
        SourceUrl = module.SourceUrl?.ToString(),
        IconUrl = module.IconUrl?.ToString(),
        RepositoryId = module.RepositoryId
    };

    /// <summary>
    /// Parses the form. Refusing here rather than in the service is deliberate: these are typing
    /// mistakes, and a refusal that costs a database round trip is one the page can make for free.
    /// </summary>
    public bool TryGetValues(
        [NotNullWhen(true)] out ModuleFormValues? values, [NotNullWhen(false)] out string? error)
    {
        values = null;

        if (string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(Name))
        {
            error = "Id and name are both required.";
            return false;
        }

        if (!FormValues.TryParseUri(SourceUrl, out var sourceUrl) ||
            !FormValues.TryParseUri(IconUrl, out var iconUrl))
        {
            error = "Source and icon must be absolute URLs, or left empty.";
            return false;
        }

        values = new ModuleFormValues(
            Id.Trim(), Name.Trim(), Description ?? string.Empty, sourceUrl, iconUrl, RepositoryId);
        error = null;
        return true;
    }
}
