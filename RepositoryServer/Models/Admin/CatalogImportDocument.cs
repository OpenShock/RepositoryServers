using System.Text.Json.Serialization;

namespace OpenShock.RepositoryServer.Models.Admin;

/// <summary>
/// A whole catalog, as an operator writes it down: chips, boards, publishers, USB devices and
/// serial filters, and desktop modules. This is what <c>/admin/import</c> reads.
/// </summary>
/// <remarks>
/// Everything cross-references by natural key — a board names its chip, a module names its
/// publisher as <c>owner/repo</c> — never by id. A file that referenced database ids could only
/// ever be written by exporting from the instance it was going to be imported into, which defeats
/// the point: this format has to be writable by hand and reusable across a fresh instance, a
/// staging one and production.
///
/// Every section is optional. A file with only <c>chips</c> and <c>boards</c> is a valid import,
/// which is what makes it reasonable to keep several small files rather than one large one.
/// </remarks>
public sealed class CatalogImportDocument
{
    public List<ChipImport> Chips { get; init; } = [];
    public List<BoardImport> Boards { get; init; } = [];
    public List<PublisherImport> Repositories { get; init; } = [];
    public List<UsbDeviceImport> UsbDevices { get; init; } = [];
    public List<UsbSerialFilterImport> UsbSerialFilters { get; init; } = [];
    public List<ModuleImport> Modules { get; init; } = [];

    [JsonIgnore]
    public bool IsEmpty =>
        Chips.Count == 0 && Boards.Count == 0 && Repositories.Count == 0 &&
        UsbDevices.Count == 0 && UsbSerialFilters.Count == 0 && Modules.Count == 0;
}

/// <param name="Name">Must match the esptool-js chip identifier exactly, e.g. <c>ESP32-S3</c>.</param>
/// <param name="Architecture"><c>xtensa</c> or <c>riscv</c>. Omitted leaves it unspecified.</param>
/// <param name="UsbDevices">
/// Native USB modes this chip exposes, as <c>vid:pid</c> hex pairs (<c>303A:1001</c>). Each must
/// also appear in the document's <c>usbDevices</c> section or already exist on the server.
/// </param>
public sealed record ChipImport(
    string Name,
    string? Architecture = null,
    List<string>? UsbDevices = null);

/// <param name="Name">
/// What a hub reports as its own board, which is its <c>boards/&lt;name&gt;.defaults</c> basename in
/// the firmware repository. A mismatch here is invisible until a release init rejects the board.
/// </param>
/// <param name="Chip">Chip name, resolved against this document and then the server.</param>
/// <param name="RequiredArtifacts">
/// Artifact types a release must supply for this board. Defaults to <c>app</c> and <c>staticfs</c>,
/// the two an OTA update needs. Accepts <c>staticfs</c> or <c>static_fs</c>.
/// </param>
/// <param name="Discontinued">
/// Discontinued boards still serve firmware to hubs already in the field; the flag only affects
/// whether they are offered for new installs.
/// </param>
/// <param name="UsbDevices">USB devices fitted to this board, as <c>vid:pid</c> hex pairs.</param>
public sealed record BoardImport(
    string Name,
    string Chip,
    List<string>? RequiredArtifacts = null,
    bool Discontinued = false,
    List<string>? UsbDevices = null);

/// <param name="Owner">GitHub owner, matched case-insensitively.</param>
/// <param name="Repo">GitHub repository name, matched case-insensitively.</param>
/// <param name="Scopes">
/// <c>publish_firmware</c>, <c>publish_modules</c>, or both. An empty list registers a repository
/// that authenticates but cannot publish anything, which is a way to stage one before granting it.
/// </param>
public sealed record PublisherImport(
    string Owner,
    string Repo,
    List<string>? Scopes = null);

/// <param name="Vid">Vendor id, hex (<c>303A</c>) or decimal.</param>
/// <param name="Pid">Product id, hex or decimal.</param>
/// <param name="Name">Display name, shown when the flashtool recognises a connected device.</param>
public sealed record UsbDeviceImport(
    string Vid,
    string Pid,
    string Name);

/// <param name="Vid">Vendor id, hex or decimal.</param>
/// <param name="Pid">
/// Product id. Omitted matches every product from that vendor, which is what CH340-style bridges
/// and native USB need, since the product id varies by chip.
/// </param>
/// <param name="Description">Free text, for a human reading the filter list.</param>
public sealed record UsbSerialFilterImport(
    string Vid,
    string? Pid = null,
    string? Description = null);

/// <param name="Id">Module id, lowercased on import to match how the upload endpoint looks it up.</param>
/// <param name="Name">Display name, shown in the desktop app's module list.</param>
/// <param name="Description">One or two sentences on what the module does.</param>
/// <param name="SourceUrl">Absolute URL to the module's source, or omitted.</param>
/// <param name="IconUrl">Absolute URL to an icon, or omitted.</param>
/// <param name="Repository">
/// Publisher allowed to publish versions, as <c>owner/repo</c>. Omitted leaves the module
/// unassigned, which closes it to every publisher rather than opening it to any.
/// </param>
public sealed record ModuleImport(
    string Id,
    string Name,
    string Description,
    string? SourceUrl = null,
    string? IconUrl = null,
    string? Repository = null);
