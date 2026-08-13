using Microsoft.EntityFrameworkCore;
using OneOf;
using OneOf.Types;
using OpenShock.RepositoryServer.Enums;
using OpenShock.RepositoryServer.RepoServerDb;
using OpenShock.RepositoryServer.RepoServerDb.Models;
using OpenShock.RepositoryServer.Utils;

namespace OpenShock.RepositoryServer.Services.Admin;

/// <summary>
/// The firmware hardware catalog: chips, boards, USB devices and USB serial filters.
/// </summary>
/// <remarks>
/// Kept together because the invariants cross between them. A chip cannot be deleted while a board
/// references it, a USB device cannot be deleted while either references it, and attaching a device
/// has to check both sides exist. Splitting these apart would mean one of them reaching into the
/// other's tables anyway.
/// </remarks>
public sealed class CatalogAdminService
{
    private readonly RepoServerContext _db;

    public CatalogAdminService(RepoServerContext db)
    {
        _db = db;
    }

    // ---- Chips ---------------------------------------------------------------------------------

    public Task<FirmwareChip[]> ListChipsAsync(CancellationToken ct = default) =>
        _db.FirmwareChips.OrderBy(c => c.Name).ToArrayAsync(ct);

    public async Task<OneOf<FirmwareChip, NameConflict>> CreateChipAsync(
        string name, FirmwareChipArchitecture? architecture, CancellationToken ct = default)
    {
        var chip = new FirmwareChip
        {
            Id = Guid.NewGuid(),
            Name = name,
            Architecture = architecture
        };

        _db.FirmwareChips.Add(chip);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (UniqueViolation.IsOn(ex, UniqueViolation.ChipNameLower))
        {
            return new NameConflict(name);
        }

        return chip;
    }

    public async Task<OneOf<Success, NotFound, NameConflict>> UpdateChipAsync(
        Guid chipId, string name, FirmwareChipArchitecture? architecture, CancellationToken ct = default)
    {
        var chip = await _db.FirmwareChips.FirstOrDefaultAsync(c => c.Id == chipId, ct);
        if (chip is null)
        {
            return new NotFound();
        }

        chip.Name = name;
        chip.Architecture = architecture;

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (UniqueViolation.IsOn(ex, UniqueViolation.ChipNameLower))
        {
            return new NameConflict(name);
        }

        return new Success();
    }

    public async Task<OneOf<Success, NotFound, InUse>> DeleteChipAsync(
        Guid chipId, CancellationToken ct = default)
    {
        // Boards carry the foreign key, so the database would refuse this anyway. Checking first turns
        // a constraint violation into an answer that names the obstacle.
        if (await _db.FirmwareBoards.AnyAsync(b => b.ChipId == chipId, ct))
        {
            return new InUse("boards");
        }

        var deleted = await _db.FirmwareChips.Where(c => c.Id == chipId).ExecuteDeleteAsync(ct);
        if (deleted <= 0)
        {
            return new NotFound();
        }

        return new Success();
    }

    // ---- Boards --------------------------------------------------------------------------------

    public Task<FirmwareBoard[]> ListBoardsAsync(CancellationToken ct = default) =>
        _db.FirmwareBoards
            .Include(b => b.ChipNavigation)
            .OrderBy(b => b.Name)
            .ToArrayAsync(ct);

    public async Task<OneOf<FirmwareBoard, ReferenceNotFound, NameConflict, InvalidName>> CreateBoardAsync(
        string name, Guid chipId, FirmwareArtifactType[] requiredArtifactTypes,
        CancellationToken ct = default)
    {
        if (!FirmwareBoardName.IsValid(name))
        {
            return new InvalidName(name);
        }

        if (!await _db.FirmwareChips.AnyAsync(c => c.Id == chipId, ct))
        {
            return new ReferenceNotFound("chip");
        }

        var board = new FirmwareBoard
        {
            Id = Guid.NewGuid(),
            Name = name,
            ChipId = chipId,
            Discontinued = false,
            RequiredArtifactTypes = requiredArtifactTypes.Distinct().ToArray()
        };

        _db.FirmwareBoards.Add(board);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (UniqueViolation.IsOn(ex, UniqueViolation.BoardNameLower))
        {
            return new NameConflict(name);
        }

        return board;
    }

    public async Task<OneOf<Success, NotFound, ReferenceNotFound, NameConflict, InvalidName>> UpdateBoardAsync(
        Guid boardId, string name, Guid chipId, FirmwareArtifactType[] requiredArtifactTypes,
        CancellationToken ct = default)
    {
        if (!FirmwareBoardName.IsValid(name))
        {
            return new InvalidName(name);
        }

        if (!await _db.FirmwareChips.AnyAsync(c => c.Id == chipId, ct))
        {
            return new ReferenceNotFound("chip");
        }

        var board = await _db.FirmwareBoards.FirstOrDefaultAsync(b => b.Id == boardId, ct);
        if (board is null)
        {
            return new NotFound();
        }

        board.Name = name;
        board.ChipId = chipId;
        board.RequiredArtifactTypes = requiredArtifactTypes.Distinct().ToArray();

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (UniqueViolation.IsOn(ex, UniqueViolation.BoardNameLower))
        {
            return new NameConflict(name);
        }

        return new Success();
    }

    /// <summary>
    /// Marks a board discontinued, or brings it back. Discontinued boards still serve firmware: hubs
    /// already in the field have to keep updating.
    /// </summary>
    public async Task<OneOf<Success, NotFound>> SetBoardDiscontinuedAsync(
        Guid boardId, bool discontinued, CancellationToken ct = default)
    {
        var board = await _db.FirmwareBoards.FirstOrDefaultAsync(b => b.Id == boardId, ct);
        if (board is null)
        {
            return new NotFound();
        }

        board.Discontinued = discontinued;
        await _db.SaveChangesAsync(ct);
        return new Success();
    }

    public async Task<OneOf<Success, NotFound, InUse>> DeleteBoardAsync(
        Guid boardId, CancellationToken ct = default)
    {
        // A board with published artifacts is part of released history. Deleting it would strand the
        // artifact rows that describe what hubs are running.
        if (await _db.FirmwareArtifacts.AnyAsync(a => a.BoardId == boardId, ct))
        {
            return new InUse("published artifacts");
        }

        var deleted = await _db.FirmwareBoards.Where(b => b.Id == boardId).ExecuteDeleteAsync(ct);
        if (deleted <= 0)
        {
            return new NotFound();
        }

        return new Success();
    }

    // ---- USB devices ---------------------------------------------------------------------------

    public Task<UsbDevice[]> ListUsbDevicesAsync(CancellationToken ct = default) =>
        _db.UsbDevices.OrderBy(d => d.Vid).ThenBy(d => d.Pid).ToArrayAsync(ct);

    /// <summary>
    /// Upserts on (vid, pid), which is the identity of a USB device, so saving a pair that already
    /// exists renames it rather than adding an ambiguous second row.
    /// </summary>
    public async Task<UsbDevice> UpsertUsbDeviceAsync(
        int vid, int pid, string name, CancellationToken ct = default)
    {
        var existing = await _db.UsbDevices.FirstOrDefaultAsync(d => d.Vid == vid && d.Pid == pid, ct);
        if (existing is not null)
        {
            existing.Name = name;
            await _db.SaveChangesAsync(ct);
            return existing;
        }

        var device = new UsbDevice
        {
            Id = Guid.NewGuid(),
            Vid = vid,
            Pid = pid,
            Name = name
        };
        _db.UsbDevices.Add(device);
        await _db.SaveChangesAsync(ct);

        return device;
    }

    public async Task<OneOf<Success, NotFound, InUse>> DeleteUsbDeviceAsync(
        Guid id, CancellationToken ct = default)
    {
        var inUse =
            await _db.FirmwareChipUsbDevices.AnyAsync(j => j.UsbDeviceId == id, ct) ||
            await _db.FirmwareBoardUsbDevices.AnyAsync(j => j.UsbDeviceId == id, ct);

        if (inUse)
        {
            return new InUse("chips or boards");
        }

        var deleted = await _db.UsbDevices.Where(d => d.Id == id).ExecuteDeleteAsync(ct);
        if (deleted <= 0)
        {
            return new NotFound();
        }

        return new Success();
    }

    // ---- USB device attachment -----------------------------------------------------------------

    public Task<FirmwareChipUsbDevice[]> ListChipUsbDevicesAsync(CancellationToken ct = default) =>
        _db.FirmwareChipUsbDevices.ToArrayAsync(ct);

    public Task<FirmwareBoardUsbDevice[]> ListBoardUsbDevicesAsync(CancellationToken ct = default) =>
        _db.FirmwareBoardUsbDevices.ToArrayAsync(ct);

    /// <summary>Idempotent: attaching an already-attached device is a no-op, not a second join row.</summary>
    public async Task<OneOf<Success, ReferenceNotFound>> AttachUsbDeviceToChipAsync(
        Guid chipId, Guid usbDeviceId, CancellationToken ct = default)
    {
        if (!await _db.FirmwareChips.AnyAsync(c => c.Id == chipId, ct))
        {
            return new ReferenceNotFound("chip");
        }

        if (!await _db.UsbDevices.AnyAsync(d => d.Id == usbDeviceId, ct))
        {
            return new ReferenceNotFound("usb device");
        }

        var exists = await _db.FirmwareChipUsbDevices
            .AnyAsync(j => j.ChipId == chipId && j.UsbDeviceId == usbDeviceId, ct);
        if (!exists)
        {
            _db.FirmwareChipUsbDevices.Add(new FirmwareChipUsbDevice
            {
                ChipId = chipId,
                UsbDeviceId = usbDeviceId
            });
            await _db.SaveChangesAsync(ct);
        }

        return new Success();
    }

    /// <summary>Detaching something that is not attached is already the desired state, so it succeeds.</summary>
    public async Task<Success> DetachUsbDeviceFromChipAsync(
        Guid chipId, Guid usbDeviceId, CancellationToken ct = default)
    {
        await _db.FirmwareChipUsbDevices
            .Where(j => j.ChipId == chipId && j.UsbDeviceId == usbDeviceId)
            .ExecuteDeleteAsync(ct);

        return new Success();
    }

    /// <inheritdoc cref="AttachUsbDeviceToChipAsync"/>
    public async Task<OneOf<Success, ReferenceNotFound>> AttachUsbDeviceToBoardAsync(
        Guid boardId, Guid usbDeviceId, CancellationToken ct = default)
    {
        if (!await _db.FirmwareBoards.AnyAsync(b => b.Id == boardId, ct))
        {
            return new ReferenceNotFound("board");
        }

        if (!await _db.UsbDevices.AnyAsync(d => d.Id == usbDeviceId, ct))
        {
            return new ReferenceNotFound("usb device");
        }

        var exists = await _db.FirmwareBoardUsbDevices
            .AnyAsync(j => j.BoardId == boardId && j.UsbDeviceId == usbDeviceId, ct);
        if (!exists)
        {
            _db.FirmwareBoardUsbDevices.Add(new FirmwareBoardUsbDevice
            {
                BoardId = boardId,
                UsbDeviceId = usbDeviceId
            });
            await _db.SaveChangesAsync(ct);
        }

        return new Success();
    }

    /// <inheritdoc cref="DetachUsbDeviceFromChipAsync"/>
    public async Task<Success> DetachUsbDeviceFromBoardAsync(
        Guid boardId, Guid usbDeviceId, CancellationToken ct = default)
    {
        await _db.FirmwareBoardUsbDevices
            .Where(j => j.BoardId == boardId && j.UsbDeviceId == usbDeviceId)
            .ExecuteDeleteAsync(ct);

        return new Success();
    }

    // ---- USB serial filters --------------------------------------------------------------------

    public Task<UsbSerialFilter[]> ListUsbSerialFiltersAsync(CancellationToken ct = default) =>
        _db.UsbSerialFilters.OrderBy(f => f.Vid).ThenBy(f => f.Pid).ToArrayAsync(ct);

    /// <summary>
    /// Upserts on (vid, pid). The unique index treats nulls as equal, so there is at most one
    /// vendor-wide row per VID.
    /// </summary>
    public async Task<UsbSerialFilter> UpsertUsbSerialFilterAsync(
        int vid, int? pid, string? description, CancellationToken ct = default)
    {
        var existing = await _db.UsbSerialFilters.FirstOrDefaultAsync(f => f.Vid == vid && f.Pid == pid, ct);
        if (existing is not null)
        {
            existing.Description = description;
            await _db.SaveChangesAsync(ct);
            return existing;
        }

        var filter = new UsbSerialFilter
        {
            Id = Guid.NewGuid(),
            Vid = vid,
            Pid = pid,
            Description = description
        };
        _db.UsbSerialFilters.Add(filter);
        await _db.SaveChangesAsync(ct);

        return filter;
    }

    public async Task<OneOf<Success, NotFound>> DeleteUsbSerialFilterAsync(
        Guid id, CancellationToken ct = default)
    {
        var deleted = await _db.UsbSerialFilters.Where(f => f.Id == id).ExecuteDeleteAsync(ct);
        if (deleted <= 0)
        {
            return new NotFound();
        }

        return new Success();
    }
}
