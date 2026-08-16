using OpenShock.RepositoryServer.Models.Admin;
using System.Text.Json;

namespace OpenShock.RepositoryServer.Tests;

/// <summary>
/// Guards <c>seed/openshock-catalog.json</c>, the file an operator imports to seed an instance.
/// </summary>
/// <remarks>
/// It is checked in, so it can rot: a board renamed in the firmware repository, a chip reference
/// that no longer resolves, a comment that breaks the parse. Catching that here costs nothing;
/// catching it at <c>/admin/import</c> means someone has already been told the seed is broken.
///
/// Deliberately has no database. These are properties of the file itself, so they run without
/// Docker — unlike <c>CatalogImportTests</c>, which exercises the same document against real
/// migrations.
/// </remarks>
public class ShippedCatalogTests
{
    // Mirrors the options the import page parses with, including the comment handling the file
    // relies on to carry its sources inline.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    private static CatalogImportDocument Load()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "RepositoryServer.slnx")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new InvalidOperationException(
                $"Could not locate RepositoryServer.slnx above '{AppContext.BaseDirectory}'.");
        }

        var path = Path.Combine(directory.FullName, "seed", "openshock-catalog.json");
        var document = JsonSerializer.Deserialize<CatalogImportDocument>(File.ReadAllText(path), JsonOptions);

        return document!;
    }

    [Test]
    public async Task Parses()
    {
        var document = Load();

        await Assert.That(document).IsNotNull();
        await Assert.That(document.IsEmpty).IsFalse();
    }

    /// <summary>
    /// The import blocks a board whose chip is neither in the file nor on the server, so a typo
    /// here would make the whole seed unappliable against a fresh instance.
    /// </summary>
    [Test]
    public async Task EveryBoardReferencesADeclaredChip()
    {
        var document = Load();
        var chips = document.Chips.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var board in document.Boards)
        {
            await Assert.That(chips.Contains(board.Chip))
                .IsTrue()
                .Because($"board '{board.Name}' references undeclared chip '{board.Chip}'");
        }
    }

    /// <summary>Same reasoning for the USB references, which block on a miss the same way.</summary>
    [Test]
    public async Task EveryUsbReferenceIsDeclared()
    {
        var document = Load();
        var devices = document.UsbDevices
            .Select(d => $"{d.Vid}:{d.Pid}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var references = document.Chips.SelectMany(c => c.UsbDevices ?? [])
            .Concat(document.Boards.SelectMany(b => b.UsbDevices ?? []));

        foreach (var reference in references)
        {
            await Assert.That(devices.Contains(reference))
                .IsTrue()
                .Because($"'{reference}' is not in the usbDevices section");
        }
    }

    /// <summary>
    /// A module naming a publisher the file does not declare would block, and an unassigned module
    /// is closed to every publisher rather than open to any.
    /// </summary>
    [Test]
    public async Task EveryModuleReferencesADeclaredPublisher()
    {
        var document = Load();
        var publishers = document.Repositories
            .Select(r => $"{r.Owner}/{r.Repo}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var module in document.Modules.Where(m => m.Repository is not null))
        {
            await Assert.That(publishers.Contains(module.Repository!))
                .IsTrue()
                .Because($"module '{module.Id}' references undeclared publisher '{module.Repository}'");
        }
    }

    /// <summary>
    /// The seed has to cover every board the firmware repository builds, or the first publish
    /// fails on whichever one was left out. Compared by count rather than by name because the
    /// board list lives in the other repository; the names themselves are checked there, by the
    /// preflight step in ci-build.
    /// </summary>
    [Test]
    public async Task CoversTheKnownBoardSet()
    {
        var document = Load();

        await Assert.That(document.Boards.Count).IsEqualTo(13);
        await Assert.That(document.Chips.Count).IsEqualTo(4);
    }
}
