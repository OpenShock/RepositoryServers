using Microsoft.EntityFrameworkCore;
using OpenShock.RepositoryServer.Enums;
using OpenShock.RepositoryServer.Models.Admin;
using OpenShock.RepositoryServer.RepoServerDb;
using OpenShock.RepositoryServer.Utils;
using System.Globalization;

namespace OpenShock.RepositoryServer.Services.Admin;

/// <summary>What importing one row would do, or did.</summary>
public enum ImportAction
{
    /// <summary>Nothing on the server matches this row's natural key.</summary>
    Create,

    /// <summary>A row exists under this key and at least one field differs.</summary>
    Update,

    /// <summary>A row exists and every field already matches. Applying it is a no-op.</summary>
    Unchanged,

    /// <summary>Cannot be applied. <see cref="ImportItem.Detail"/> says why.</summary>
    Blocked
}

/// <param name="Section">Which part of the document, for grouping the preview.</param>
/// <param name="Key">The row's natural key, as the operator wrote it.</param>
/// <param name="Action">What importing this row would do, or did.</param>
/// <param name="Detail">What changes, or why it is blocked. Empty when unchanged.</param>
public sealed record ImportItem(string Section, string Key, ImportAction Action, string Detail);

/// <summary>
/// What an import would do, computed against the current database without writing anything.
/// </summary>
public sealed record ImportPlan(IReadOnlyList<ImportItem> Items)
{
    public int Creates => Items.Count(i => i.Action == ImportAction.Create);
    public int Updates => Items.Count(i => i.Action == ImportAction.Update);
    public int Unchanged => Items.Count(i => i.Action == ImportAction.Unchanged);
    public int Blocked => Items.Count(i => i.Action == ImportAction.Blocked);

    /// <summary>
    /// A blocked row fails the whole import rather than being skipped. A half-applied catalog is
    /// worse than an unapplied one: it looks seeded, and the missing piece only surfaces when a
    /// publish rejects a board nobody knew was absent.
    /// </summary>
    public bool CanApply => Blocked == 0 && Creates + Updates > 0;
}

/// <summary>
/// Reads a <see cref="CatalogImportDocument"/> and applies it through the admin services.
/// </summary>
/// <remarks>
/// It writes nothing itself. Every change goes through <see cref="CatalogAdminService"/>,
/// <see cref="PublisherAdminService"/> or <see cref="ModuleAdminService"/>, so an import is held to
/// the same invariants as the equivalent clicking: board names are validated the same way, chip
/// references are checked the same way, and a rename that would collide is refused the same way.
/// Bulk is the only thing that differs.
///
/// It also never deletes. Removing a row is where the damage is — a chip a board still references,
/// a board published artifacts still point at — and those decisions want the individual pages,
/// which name what holds the reference. So a row dropped from the file stays on the server.
/// </remarks>
public sealed class CatalogImportService
{
    private readonly RepoServerContext _db;
    private readonly CatalogAdminService _catalog;
    private readonly PublisherAdminService _publishers;
    private readonly ModuleAdminService _modules;

    public CatalogImportService(
        RepoServerContext db,
        CatalogAdminService catalog,
        PublisherAdminService publishers,
        ModuleAdminService modules)
    {
        _db = db;
        _catalog = catalog;
        _publishers = publishers;
        _modules = modules;
    }

    /// <summary>
    /// Works out what <paramref name="document"/> would change. Read-only, so the preview it feeds
    /// can be shown and abandoned.
    /// </summary>
    public async Task<ImportPlan> PlanAsync(CatalogImportDocument document, CancellationToken ct = default)
    {
        var items = new List<ImportItem>();
        var state = await ReadStateAsync(ct);

        // Chip names declared in this document count as resolvable for boards below, even though
        // they do not exist yet. Otherwise every board in a fresh-instance import would preview as
        // blocked, and the preview would be useless in exactly the case it matters most.
        var knownChips = new HashSet<string>(state.Chips.Keys, StringComparer.OrdinalIgnoreCase);
        foreach (var chip in document.Chips)
        {
            knownChips.Add(chip.Name);
        }

        var knownDevices = new HashSet<string>(state.UsbDevices.Keys, StringComparer.OrdinalIgnoreCase);
        foreach (var device in document.UsbDevices)
        {
            if (TryParseVidPid(device.Vid, device.Pid, out var vid, out var pid))
            {
                knownDevices.Add(VidPidKey(vid, pid));
            }
        }

        var knownRepositories = new HashSet<string>(state.Repositories.Keys, StringComparer.OrdinalIgnoreCase);
        foreach (var publisher in document.Repositories)
        {
            knownRepositories.Add($"{publisher.Owner}/{publisher.Repo}");
        }

        foreach (var chip in document.Chips)
        {
            items.Add(PlanChip(chip, state, knownDevices));
        }

        foreach (var device in document.UsbDevices)
        {
            items.Add(PlanUsbDevice(device, state));
        }

        foreach (var filter in document.UsbSerialFilters)
        {
            items.Add(PlanUsbSerialFilter(filter, state));
        }

        foreach (var board in document.Boards)
        {
            items.Add(PlanBoard(board, state, knownChips, knownDevices));
        }

        foreach (var publisher in document.Repositories)
        {
            items.Add(PlanPublisher(publisher, state));
        }

        foreach (var module in document.Modules)
        {
            items.Add(PlanModule(module, state, knownRepositories));
        }

        items.AddRange(DuplicateKeyErrors(document));

        return new ImportPlan(items);
    }

    /// <summary>
    /// Applies the document. Ordered by dependency — chips and USB devices before the boards that
    /// reference them, publishers before the modules they own — so a single pass resolves
    /// everything a fresh instance needs without a second reconciliation.
    /// </summary>
    /// <remarks>
    /// Re-plans first and refuses if anything is blocked, because the preview the operator approved
    /// was computed against a database that may have moved since.
    /// </remarks>
    public async Task<ImportPlan> ApplyAsync(CatalogImportDocument document, CancellationToken ct = default)
    {
        var plan = await PlanAsync(document, ct);
        if (!plan.CanApply)
        {
            return plan;
        }

        var results = new List<ImportItem>();

        foreach (var chip in document.Chips)
        {
            results.Add(await ApplyChipAsync(chip, ct));
        }

        foreach (var device in document.UsbDevices)
        {
            if (!TryParseVidPid(device.Vid, device.Pid, out var vid, out var pid))
            {
                results.Add(Blocked("usb device", $"{device.Vid}:{device.Pid}", "vid/pid is not a number"));
                continue;
            }

            await _catalog.UpsertUsbDeviceAsync(vid, pid, device.Name, ct);
            results.Add(Done("usb device", VidPidKey(vid, pid), device.Name));
        }

        foreach (var filter in document.UsbSerialFilters)
        {
            if (!TryParseVid(filter.Vid, out var vid) || !TryParseOptionalPid(filter.Pid, out var pid))
            {
                results.Add(Blocked("serial filter", $"{filter.Vid}:{filter.Pid}", "vid/pid is not a number"));
                continue;
            }

            await _catalog.UpsertUsbSerialFilterAsync(vid, pid, filter.Description, ct);
            results.Add(Done("serial filter", VidPidKey(vid, pid), filter.Description ?? string.Empty));
        }

        // Re-read so boards resolve against the chips just created.
        var state = await ReadStateAsync(ct);

        foreach (var board in document.Boards)
        {
            results.Add(await ApplyBoardAsync(board, state, ct));
        }

        // Attachments come after both sides exist. They are idempotent, so re-importing a file
        // whose devices are already attached adds nothing.
        state = await ReadStateAsync(ct);
        results.AddRange(await ApplyAttachmentsAsync(document, state, ct));

        foreach (var publisher in document.Repositories)
        {
            var scopes = ParseScopes(publisher.Scopes, out var badScope);
            if (badScope is not null)
            {
                results.Add(Blocked("publisher", $"{publisher.Owner}/{publisher.Repo}", $"unknown scope '{badScope}'"));
                continue;
            }

            await _publishers.UpsertAsync(RepositoryProvider.Github, publisher.Owner, publisher.Repo, scopes, ct);
            results.Add(Done("publisher", $"{publisher.Owner}/{publisher.Repo}", DescribeScopes(scopes)));
        }

        state = await ReadStateAsync(ct);

        foreach (var module in document.Modules)
        {
            results.Add(await ApplyModuleAsync(module, state, ct));
        }

        // Carry the planned action onto the result, so a row that was created still reads "create"
        // afterwards. Applying cannot tell the difference — every write here is an upsert — but the
        // operator is reading the same table they just approved, and it should not have changed
        // shape underneath them. Indexed by assignment rather than ToDictionary: a document with
        // duplicate keys produces two plan entries under one key, and that is a blocked import
        // returned above, not a crash here.
        var plannedActions = new Dictionary<(string Section, string Key), ImportAction>();
        foreach (var item in plan.Items)
        {
            plannedActions[(item.Section, item.Key)] = item.Action;
        }

        var finalized = results
            .Select(r => r.Action != ImportAction.Blocked
                         && plannedActions.TryGetValue((r.Section, r.Key), out var planned)
                ? r with { Action = planned }
                : r)
            .ToList();

        return new ImportPlan(finalized);
    }

    // -----------------------------------------------------------------------------------------
    // Planning
    // -----------------------------------------------------------------------------------------

    private ImportItem PlanChip(ChipImport chip, ServerState state, HashSet<string> knownDevices)
    {
        if (string.IsNullOrWhiteSpace(chip.Name))
        {
            return Blocked("chip", "(unnamed)", "name is required");
        }

        if (!TryParseArchitecture(chip.Architecture, out var architecture))
        {
            return Blocked("chip", chip.Name, $"unknown architecture '{chip.Architecture}', expected xtensa or riscv");
        }

        foreach (var reference in chip.UsbDevices ?? [])
        {
            if (!knownDevices.Contains(NormalizeVidPid(reference)))
            {
                return Blocked("chip", chip.Name, $"usb device '{reference}' is neither in this file nor on the server");
            }
        }

        if (!state.Chips.TryGetValue(chip.Name, out var existing))
        {
            return new ImportItem("chip", chip.Name, ImportAction.Create, Describe(architecture));
        }

        return existing.Architecture == architecture
            ? new ImportItem("chip", chip.Name, ImportAction.Unchanged, string.Empty)
            : new ImportItem("chip", chip.Name, ImportAction.Update,
                $"architecture {Describe(existing.Architecture)} -> {Describe(architecture)}");
    }

    private ImportItem PlanBoard(
        BoardImport board, ServerState state, HashSet<string> knownChips, HashSet<string> knownDevices)
    {
        if (!FirmwareBoardName.IsValid(board.Name))
        {
            return Blocked("board", board.Name, "name must start alphanumeric and contain only letters, digits, dot, underscore or hyphen");
        }

        if (!knownChips.Contains(board.Chip))
        {
            return Blocked("board", board.Name, $"chip '{board.Chip}' is neither in this file nor on the server");
        }

        var artifacts = ParseArtifactTypes(board.RequiredArtifacts, out var badArtifact);
        if (badArtifact is not null)
        {
            return Blocked("board", board.Name, $"unknown artifact type '{badArtifact}'");
        }

        foreach (var reference in board.UsbDevices ?? [])
        {
            if (!knownDevices.Contains(NormalizeVidPid(reference)))
            {
                return Blocked("board", board.Name, $"usb device '{reference}' is neither in this file nor on the server");
            }
        }

        if (!state.Boards.TryGetValue(board.Name, out var existing))
        {
            return new ImportItem("board", board.Name, ImportAction.Create,
                $"{board.Chip}, requires {DescribeArtifacts(artifacts)}");
        }

        var changes = new List<string>();

        var existingChipName = state.ChipNamesById.GetValueOrDefault(existing.ChipId, "?");
        if (!string.Equals(existingChipName, board.Chip, StringComparison.OrdinalIgnoreCase))
        {
            changes.Add($"chip {existingChipName} -> {board.Chip}");
        }

        if (!existing.RequiredArtifactTypes.OrderBy(a => a).SequenceEqual(artifacts.OrderBy(a => a)))
        {
            changes.Add($"requires {DescribeArtifacts(existing.RequiredArtifactTypes)} -> {DescribeArtifacts(artifacts)}");
        }

        if (existing.Discontinued != board.Discontinued)
        {
            changes.Add(board.Discontinued ? "discontinued" : "back in production");
        }

        return changes.Count == 0
            ? new ImportItem("board", board.Name, ImportAction.Unchanged, string.Empty)
            : new ImportItem("board", board.Name, ImportAction.Update, string.Join(", ", changes));
    }

    private static ImportItem PlanUsbDevice(UsbDeviceImport device, ServerState state)
    {
        if (!TryParseVidPid(device.Vid, device.Pid, out var vid, out var pid))
        {
            return Blocked("usb device", $"{device.Vid}:{device.Pid}", "vid/pid is not a number");
        }

        var key = VidPidKey(vid, pid);
        if (!state.UsbDevices.TryGetValue(key, out var existing))
        {
            return new ImportItem("usb device", key, ImportAction.Create, device.Name);
        }

        return existing.Name == device.Name
            ? new ImportItem("usb device", key, ImportAction.Unchanged, string.Empty)
            : new ImportItem("usb device", key, ImportAction.Update, $"{existing.Name} -> {device.Name}");
    }

    private static ImportItem PlanUsbSerialFilter(UsbSerialFilterImport filter, ServerState state)
    {
        if (!TryParseVid(filter.Vid, out var vid) || !TryParseOptionalPid(filter.Pid, out var pid))
        {
            return Blocked("serial filter", $"{filter.Vid}:{filter.Pid}", "vid/pid is not a number");
        }

        var key = VidPidKey(vid, pid);
        if (!state.UsbSerialFilters.TryGetValue(key, out var existing))
        {
            return new ImportItem("serial filter", key, ImportAction.Create, filter.Description ?? string.Empty);
        }

        return existing.Description == filter.Description
            ? new ImportItem("serial filter", key, ImportAction.Unchanged, string.Empty)
            : new ImportItem("serial filter", key, ImportAction.Update,
                $"{existing.Description ?? "(none)"} -> {filter.Description ?? "(none)"}");
    }

    private static ImportItem PlanPublisher(PublisherImport publisher, ServerState state)
    {
        if (string.IsNullOrWhiteSpace(publisher.Owner) || string.IsNullOrWhiteSpace(publisher.Repo))
        {
            return Blocked("publisher", $"{publisher.Owner}/{publisher.Repo}", "owner and repo are required");
        }

        var scopes = ParseScopes(publisher.Scopes, out var badScope);
        if (badScope is not null)
        {
            return Blocked("publisher", $"{publisher.Owner}/{publisher.Repo}",
                $"unknown scope '{badScope}', expected publish_firmware or publish_modules");
        }

        var key = $"{publisher.Owner}/{publisher.Repo}";
        if (!state.Repositories.TryGetValue(key, out var existing))
        {
            return new ImportItem("publisher", key, ImportAction.Create, DescribeScopes(scopes));
        }

        // Scopes are replaced, not merged, so a grant made through the Publishers page and not
        // written into the file is revoked here. Spelled out in the preview because it is the one
        // thing an import can take away.
        return existing.Scopes.OrderBy(s => s).SequenceEqual(scopes.OrderBy(s => s))
            ? new ImportItem("publisher", key, ImportAction.Unchanged, string.Empty)
            : new ImportItem("publisher", key, ImportAction.Update,
                $"scopes {DescribeScopes(existing.Scopes)} -> {DescribeScopes(scopes)}");
    }

    private static ImportItem PlanModule(ModuleImport module, ServerState state, HashSet<string> knownRepositories)
    {
        if (string.IsNullOrWhiteSpace(module.Id))
        {
            return Blocked("module", "(unnamed)", "id is required");
        }

        if (module.Repository is { } repository && !knownRepositories.Contains(repository))
        {
            return Blocked("module", module.Id,
                $"publisher '{repository}' is neither in this file nor on the server");
        }

        if (!TryParseUri(module.SourceUrl, out _) || !TryParseUri(module.IconUrl, out _))
        {
            return Blocked("module", module.Id, "sourceUrl / iconUrl must be absolute URLs");
        }

        var id = module.Id.ToLowerInvariant();
        if (!state.Modules.TryGetValue(id, out var existing))
        {
            return new ImportItem("module", id, ImportAction.Create,
                module.Repository is null ? "unassigned, no publisher may write to it" : $"owned by {module.Repository}");
        }

        var changes = new List<string>();
        if (existing.Name != module.Name) changes.Add("name");
        if (existing.Description != module.Description) changes.Add("description");

        var existingOwner = existing.RepositoryId is { } ownerId
            ? state.RepositoryNamesById.GetValueOrDefault(ownerId)
            : null;
        if (!string.Equals(existingOwner, module.Repository, StringComparison.OrdinalIgnoreCase))
        {
            changes.Add($"publisher {existingOwner ?? "(unassigned)"} -> {module.Repository ?? "(unassigned)"}");
        }

        return changes.Count == 0
            ? new ImportItem("module", id, ImportAction.Unchanged, string.Empty)
            : new ImportItem("module", id, ImportAction.Update, string.Join(", ", changes));
    }

    /// <summary>
    /// Two rows sharing a natural key would apply in file order and leave the last one winning,
    /// silently. Reported as blocked so the file gets fixed rather than half-applied.
    /// </summary>
    private static IEnumerable<ImportItem> DuplicateKeyErrors(CatalogImportDocument document)
    {
        foreach (var duplicate in Duplicates(document.Chips.Select(c => c.Name)))
        {
            yield return Blocked("chip", duplicate, "listed more than once");
        }

        foreach (var duplicate in Duplicates(document.Boards.Select(b => b.Name)))
        {
            yield return Blocked("board", duplicate, "listed more than once");
        }

        foreach (var duplicate in Duplicates(document.Repositories.Select(r => $"{r.Owner}/{r.Repo}")))
        {
            yield return Blocked("publisher", duplicate, "listed more than once");
        }

        foreach (var duplicate in Duplicates(document.Modules.Select(m => m.Id)))
        {
            yield return Blocked("module", duplicate, "listed more than once");
        }
    }

    private static IEnumerable<string> Duplicates(IEnumerable<string> keys) =>
        keys.GroupBy(k => k, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key);

    // -----------------------------------------------------------------------------------------
    // Applying
    // -----------------------------------------------------------------------------------------

    private async Task<ImportItem> ApplyChipAsync(ChipImport chip, CancellationToken ct)
    {
        TryParseArchitecture(chip.Architecture, out var architecture);

        var existing = await _db.FirmwareChips
            .FirstOrDefaultAsync(c => c.Name.ToLower() == chip.Name.ToLower(), ct);

        if (existing is null)
        {
            var created = await _catalog.CreateChipAsync(chip.Name, architecture, ct);
            return created.Match(
                _ => Done("chip", chip.Name, Describe(architecture)),
                conflict => Blocked("chip", chip.Name, $"name already taken by '{conflict.Name}'"));
        }

        var updated = await _catalog.UpdateChipAsync(existing.Id, chip.Name, architecture, ct);
        return updated.Match(
            _ => Done("chip", chip.Name, Describe(architecture)),
            _ => Blocked("chip", chip.Name, "disappeared while importing"),
            conflict => Blocked("chip", chip.Name, $"name already taken by '{conflict.Name}'"));
    }

    private async Task<ImportItem> ApplyBoardAsync(BoardImport board, ServerState state, CancellationToken ct)
    {
        if (!state.Chips.TryGetValue(board.Chip, out var chip))
        {
            return Blocked("board", board.Name, $"chip '{board.Chip}' not found");
        }

        var artifacts = ParseArtifactTypes(board.RequiredArtifacts, out _);

        if (!state.Boards.TryGetValue(board.Name, out var existing))
        {
            var created = await _catalog.CreateBoardAsync(board.Name, chip.Id, artifacts, ct);
            var result = created.Match(
                _ => (ImportItem?)null,
                reference => Blocked("board", board.Name, $"{reference.Reference} not found"),
                conflict => Blocked("board", board.Name, $"name already taken by '{conflict.Name}'"),
                invalid => Blocked("board", board.Name, $"'{invalid.Name}' is not a usable board name"));

            if (result is not null)
            {
                return result;
            }

            // Discontinued is not part of creation — a board is created in production and moved out
            // of it — so a file that declares one discontinued needs the second call.
            if (board.Discontinued)
            {
                var fresh = await _db.FirmwareBoards
                    .FirstAsync(b => b.Name.ToLower() == board.Name.ToLower(), ct);
                await _catalog.SetBoardDiscontinuedAsync(fresh.Id, true, ct);
            }

            return Done("board", board.Name, $"{board.Chip}, requires {DescribeArtifacts(artifacts)}");
        }

        var updated = await _catalog.UpdateBoardAsync(existing.Id, board.Name, chip.Id, artifacts, ct);
        var failure = updated.Match(
            _ => (ImportItem?)null,
            _ => Blocked("board", board.Name, "disappeared while importing"),
            reference => Blocked("board", board.Name, $"{reference.Reference} not found"),
            conflict => Blocked("board", board.Name, $"name already taken by '{conflict.Name}'"),
            invalid => Blocked("board", board.Name, $"'{invalid.Name}' is not a usable board name"));

        if (failure is not null)
        {
            return failure;
        }

        if (existing.Discontinued != board.Discontinued)
        {
            await _catalog.SetBoardDiscontinuedAsync(existing.Id, board.Discontinued, ct);
        }

        return Done("board", board.Name, $"{board.Chip}, requires {DescribeArtifacts(artifacts)}");
    }

    private async Task<List<ImportItem>> ApplyAttachmentsAsync(
        CatalogImportDocument document, ServerState state, CancellationToken ct)
    {
        var results = new List<ImportItem>();

        foreach (var chip in document.Chips)
        {
            if (chip.UsbDevices is not { Count: > 0 } references) continue;
            if (!state.Chips.TryGetValue(chip.Name, out var chipRow)) continue;

            foreach (var reference in references)
            {
                if (!state.UsbDevices.TryGetValue(NormalizeVidPid(reference), out var device))
                {
                    results.Add(Blocked("chip usb", $"{chip.Name} -> {reference}", "usb device not found"));
                    continue;
                }

                await _catalog.AttachUsbDeviceToChipAsync(chipRow.Id, device.Id, ct);
                results.Add(Done("chip usb", $"{chip.Name} -> {device.Name}", string.Empty));
            }
        }

        foreach (var board in document.Boards)
        {
            if (board.UsbDevices is not { Count: > 0 } references) continue;
            if (!state.Boards.TryGetValue(board.Name, out var boardRow)) continue;

            foreach (var reference in references)
            {
                if (!state.UsbDevices.TryGetValue(NormalizeVidPid(reference), out var device))
                {
                    results.Add(Blocked("board usb", $"{board.Name} -> {reference}", "usb device not found"));
                    continue;
                }

                await _catalog.AttachUsbDeviceToBoardAsync(boardRow.Id, device.Id, ct);
                results.Add(Done("board usb", $"{board.Name} -> {device.Name}", string.Empty));
            }
        }

        return results;
    }

    private async Task<ImportItem> ApplyModuleAsync(ModuleImport module, ServerState state, CancellationToken ct)
    {
        Guid? repositoryId = null;
        if (module.Repository is { } reference)
        {
            if (!state.Repositories.TryGetValue(reference, out var repository))
            {
                return Blocked("module", module.Id, $"publisher '{reference}' not found");
            }

            repositoryId = repository.Id;
        }

        TryParseUri(module.SourceUrl, out var sourceUrl);
        TryParseUri(module.IconUrl, out var iconUrl);

        var result = await _modules.UpsertAsync(
            module.Id.ToLowerInvariant(), module.Name, module.Description, sourceUrl, iconUrl, repositoryId, ct);

        return result.Match(
            _ => Done("module", module.Id.ToLowerInvariant(),
                module.Repository is null ? "unassigned" : $"owned by {module.Repository}"),
            missing => Blocked("module", module.Id, $"{missing.Reference} not found"));
    }

    // -----------------------------------------------------------------------------------------
    // Current state
    // -----------------------------------------------------------------------------------------

    private sealed record ServerState(
        Dictionary<string, RepoServerDb.Models.FirmwareChip> Chips,
        Dictionary<Guid, string> ChipNamesById,
        Dictionary<string, RepoServerDb.Models.FirmwareBoard> Boards,
        Dictionary<string, RepoServerDb.Models.UsbDevice> UsbDevices,
        Dictionary<string, RepoServerDb.Models.UsbSerialFilter> UsbSerialFilters,
        Dictionary<string, RepoServerDb.Models.SourceRepository> Repositories,
        Dictionary<Guid, string> RepositoryNamesById,
        Dictionary<string, RepoServerDb.Models.Module> Modules);

    private async Task<ServerState> ReadStateAsync(CancellationToken ct)
    {
        var chips = await _db.FirmwareChips.AsNoTracking().ToArrayAsync(ct);
        var boards = await _db.FirmwareBoards.AsNoTracking().ToArrayAsync(ct);
        var devices = await _db.UsbDevices.AsNoTracking().ToArrayAsync(ct);
        var filters = await _db.UsbSerialFilters.AsNoTracking().ToArrayAsync(ct);
        var repositories = await _db.Repositories.AsNoTracking().ToArrayAsync(ct);
        var modules = await _db.Modules.AsNoTracking().ToArrayAsync(ct);

        return new ServerState(
            chips.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase),
            chips.ToDictionary(c => c.Id, c => c.Name),
            boards.ToDictionary(b => b.Name, StringComparer.OrdinalIgnoreCase),
            devices.ToDictionary(d => VidPidKey(d.Vid, d.Pid)),
            filters.ToDictionary(f => VidPidKey(f.Vid, f.Pid)),
            repositories.ToDictionary(r => $"{r.Owner}/{r.Repo}", StringComparer.OrdinalIgnoreCase),
            repositories.ToDictionary(r => r.Id, r => $"{r.Owner}/{r.Repo}"),
            modules.ToDictionary(m => m.Id, StringComparer.OrdinalIgnoreCase));
    }

    // -----------------------------------------------------------------------------------------
    // Parsing
    //
    // Ids are written the way datasheets write them: hex, with or without the 0x prefix.
    // -----------------------------------------------------------------------------------------

    private static bool TryParseVidPid(string vidText, string pidText, out int vid, out int pid)
    {
        pid = 0;
        return TryParseId(vidText, out vid) && TryParseId(pidText, out pid);
    }

    private static bool TryParseVid(string vidText, out int vid) => TryParseId(vidText, out vid);

    private static bool TryParseOptionalPid(string? pidText, out int? pid)
    {
        if (string.IsNullOrWhiteSpace(pidText))
        {
            pid = null;
            return true;
        }

        if (!TryParseId(pidText, out var parsed))
        {
            pid = null;
            return false;
        }

        pid = parsed;
        return true;
    }

    private static bool TryParseId(string? text, out int value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        // Always hex, with an optional 0x. Accepting decimal too sounds accommodating and is not:
        // "1001" is a valid hex product id and a valid decimal number, and nothing in the string
        // says which was meant. Guessing from the characters — digit-only means decimal — silently
        // turns 1001 into 0x03E9, 6001 into 0x1771 and 0403 into 0x0193. All three store cleanly,
        // match no real device, and say nothing until a board fails to be recognised. Hex is what
        // datasheets, lsusb, esptool and the admin pages already use.
        var trimmed = text.Trim();
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[2..];
        }

        return int.TryParse(trimmed, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
    }

    private static string VidPidKey(int vid, int? pid) =>
        pid is { } p ? $"{vid:X4}:{p:X4}" : $"{vid:X4}:*";

    /// <summary>Normalizes a written <c>vid:pid</c> reference to the key form used for lookups.</summary>
    private static string NormalizeVidPid(string reference)
    {
        var parts = reference.Split(':', 2);
        if (parts.Length != 2 || !TryParseId(parts[0], out var vid))
        {
            return reference;
        }

        return TryParseId(parts[1], out var pid) ? VidPidKey(vid, pid) : VidPidKey(vid, null);
    }

    private static bool TryParseArchitecture(string? text, out FirmwareChipArchitecture? architecture)
    {
        architecture = null;
        if (string.IsNullOrWhiteSpace(text)) return true;

        // riscv, risc-v and risc_v are all the same thing written three ways, and which one an
        // operator reaches for is a coin flip.
        var normalized = text.Trim().Replace("-", string.Empty).Replace("_", string.Empty);
        switch (normalized.ToLowerInvariant())
        {
            case "xtensa":
                architecture = FirmwareChipArchitecture.Xtensa;
                return true;
            case "riscv":
                architecture = FirmwareChipArchitecture.RiscV;
                return true;
            default:
                return false;
        }
    }

    private static FirmwareArtifactType[] ParseArtifactTypes(List<string>? names, out string? invalid)
    {
        invalid = null;
        if (names is not { Count: > 0 })
        {
            // What an OTA update needs. Every board so far requires exactly these, so making it the
            // default keeps the common board entry down to a name and a chip.
            return [FirmwareArtifactType.App, FirmwareArtifactType.StaticFs];
        }

        var parsed = new List<FirmwareArtifactType>();
        foreach (var name in names)
        {
            var normalized = name.Trim().Replace("_", string.Empty).ToLowerInvariant();
            switch (normalized)
            {
                case "app": parsed.Add(FirmwareArtifactType.App); break;
                case "staticfs": parsed.Add(FirmwareArtifactType.StaticFs); break;
                case "merged": parsed.Add(FirmwareArtifactType.Merged); break;
                case "bootloader": parsed.Add(FirmwareArtifactType.Bootloader); break;
                case "partitions": parsed.Add(FirmwareArtifactType.Partitions); break;
                default:
                    invalid = name;
                    return [];
            }
        }

        return parsed.Distinct().ToArray();
    }

    private static RepositoryScope[] ParseScopes(List<string>? names, out string? invalid)
    {
        invalid = null;
        if (names is not { Count: > 0 })
        {
            return [];
        }

        var parsed = new List<RepositoryScope>();
        foreach (var name in names)
        {
            var normalized = name.Trim().Replace("_", string.Empty).ToLowerInvariant();
            switch (normalized)
            {
                case "publishfirmware": parsed.Add(RepositoryScope.PublishFirmware); break;
                case "publishmodules": parsed.Add(RepositoryScope.PublishModules); break;
                default:
                    invalid = name;
                    return [];
            }
        }

        return parsed.Distinct().ToArray();
    }

    private static bool TryParseUri(string? text, out Uri? uri)
    {
        uri = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        return Uri.TryCreate(text, UriKind.Absolute, out uri);
    }

    // -----------------------------------------------------------------------------------------
    // Formatting
    // -----------------------------------------------------------------------------------------

    private static ImportItem Done(string section, string key, string detail) =>
        new(section, key, ImportAction.Update, detail);

    private static ImportItem Blocked(string section, string key, string reason) =>
        new(section, key, ImportAction.Blocked, reason);

    private static string Describe(FirmwareChipArchitecture? architecture) =>
        architecture?.ToString().ToLowerInvariant() ?? "unspecified";

    private static string DescribeArtifacts(IEnumerable<FirmwareArtifactType> artifacts) =>
        string.Join("+", artifacts.OrderBy(a => a).Select(a => a.ToString().ToLowerInvariant()));

    private static string DescribeScopes(IEnumerable<RepositoryScope> scopes)
    {
        var names = scopes.Select(s => s.ToScopeClaim()).ToArray();
        return names.Length == 0 ? "no scopes" : string.Join(", ", names);
    }
}
