using Microsoft.EntityFrameworkCore;
using OpenShock.RepositoryServer.RepoServerDb;

namespace OpenShock.RepositoryServer.Utils;

/// <summary>
/// A board resolved from a public reference: the internal primary key plus the canonical name
/// as stored, which is what goes back out in responses and artifact paths.
/// </summary>
public readonly record struct ResolvedBoard(Guid Id, string Name);

/// <summary>
/// Resolution of the public board identifier used by the single-board firmware endpoints.
/// </summary>
/// <remarks>
/// This is the single chokepoint through which every public board reference passes, which is what
/// keeps board aliases an additive change: point alternate names at a canonical board here, and callers
/// keep receiving the canonical name with no change to storage layout or response shapes.
/// See firmware-api-spec.md §4.2.
/// </remarks>
public static class FirmwareBoardLookup
{
    /// <summary>
    /// Resolves a public board reference. The reference is either the board's
    /// <see cref="FirmwareBoard.Name"/> — what hubs compile in, e.g. <c>"Wemos-D1-Mini-ESP32"</c> —
    /// or the raw <see cref="FirmwareBoard.Id"/> for callers that already hold one (admin tooling).
    /// Name matching is case-insensitive; the canonical stored name is returned either way.
    /// </summary>
    /// <returns>The resolved board, or <c>null</c> when no board matches.</returns>
    public static async Task<ResolvedBoard?> ResolveBoardAsync(this RepoServerContext db, string boardRef, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(boardRef))
        {
            return null;
        }

        if (Guid.TryParse(boardRef, out var boardId))
        {
            return await db.FirmwareBoards
                .Where(b => b.Id == boardId)
                .Select(b => (ResolvedBoard?)new ResolvedBoard(b.Id, b.Name))
                .FirstOrDefaultAsync(ct);
        }

        // Name carries a case-sensitive unique index, so a case-insensitive match can in principle
        // hit more than one row. Order by name to keep the choice deterministic.
        var lowered = boardRef.ToLowerInvariant();
        return await db.FirmwareBoards
            .Where(b => b.Name.ToLower() == lowered)
            .OrderBy(b => b.Name)
            .Select(b => (ResolvedBoard?)new ResolvedBoard(b.Id, b.Name))
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Bulk form of <see cref="ResolveBoardAsync"/>, for callers that declare a whole set of boards
    /// at once (release init). Loads the board table once rather than issuing a query per reference —
    /// it holds a handful of rows.
    /// </summary>
    /// <returns>
    /// The boards that resolved, and the references that matched nothing — the latter in input order,
    /// so the caller can name them back to the operator.
    /// </returns>
    public static async Task<(List<ResolvedBoard> Resolved, List<string> Unknown)> ResolveBoardsAsync(
        this RepoServerContext db, IEnumerable<string> boardRefs, CancellationToken ct)
    {
        var all = await db.FirmwareBoards
            .OrderBy(b => b.Name)
            .Select(b => new ResolvedBoard(b.Id, b.Name))
            .ToListAsync(ct);

        var byId = all.ToDictionary(b => b.Id);
        var byName = new Dictionary<string, ResolvedBoard>(StringComparer.OrdinalIgnoreCase);
        foreach (var board in all)
        {
            // Ordered by name above, so a case-insensitive collision resolves deterministically.
            byName.TryAdd(board.Name, board);
        }

        var resolved = new List<ResolvedBoard>();
        var unknown = new List<string>();
        var seen = new HashSet<Guid>();

        foreach (var boardRef in boardRefs)
        {
            ResolvedBoard? match = Guid.TryParse(boardRef, out var id)
                ? byId.TryGetValue(id, out var byIdMatch) ? byIdMatch : null
                : byName.TryGetValue(boardRef, out var byNameMatch) ? byNameMatch : null;

            if (match is not { } board)
            {
                unknown.Add(boardRef);
                continue;
            }

            if (seen.Add(board.Id))
            {
                resolved.Add(board);
            }
        }

        return (resolved, unknown);
    }

    /// <summary>
    /// Maps board ids to their names. Needed wherever a storage path has to be rebuilt from rows
    /// that only carry the board id — staged artifacts, which have no board navigation property.
    /// </summary>
    public static async Task<Dictionary<Guid, string>> GetBoardNamesAsync(
        this RepoServerContext db, IEnumerable<Guid> boardIds, CancellationToken ct)
    {
        var ids = boardIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        return await db.FirmwareBoards
            .Where(b => ids.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id, b => b.Name, ct);
    }
}
