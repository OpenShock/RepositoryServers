using OpenShock.RepositoryServer.Utils;

namespace OpenShock.RepositoryServer.Tests.Utils;

public class ChangelogParserTests
{
    [Test]
    public async Task Parse_Empty_ReturnsEmptyError()
    {
        var result = ChangelogParser.Parse("");
        await Assert.That(result.IsT1).IsTrue();
        await Assert.That(result.AsT1).IsEqualTo(ChangelogParseError.Empty);
    }

    [Test]
    public async Task Parse_Whitespace_ReturnsEmptyError()
    {
        var result = ChangelogParser.Parse("   \n\n\n   ");
        await Assert.That(result.IsT1).IsTrue();
        await Assert.That(result.AsT1).IsEqualTo(ChangelogParseError.Empty);
    }

    [Test]
    public async Task Parse_NoHeadings_ReturnsNoHeadingsError()
    {
        var result = ChangelogParser.Parse("Just some text\nwithout headings.");
        await Assert.That(result.IsT1).IsTrue();
        await Assert.That(result.AsT1).IsEqualTo(ChangelogParseError.NoHeadings);
    }

    [Test]
    public async Task Parse_AllSectionsEmpty_ReturnsAllSectionsEmptyError()
    {
        var result = ChangelogParser.Parse("### Info\n\n### Warning\n\n### Breaking\n");
        await Assert.That(result.IsT1).IsTrue();
        await Assert.That(result.AsT1).IsEqualTo(ChangelogParseError.AllSectionsEmpty);
    }

    [Test]
    public async Task Parse_MapsBreakingWarningInfo_ToLowercaseEnumNames()
    {
        var input = "### Breaking\nLorem\n### Warning\nIpsum\n### Info\nDolor";
        var result = ChangelogParser.Parse(input);

        await Assert.That(result.IsT0).IsTrue();
        var notes = result.AsT0;
        await Assert.That(notes).HasCount(3);
        await Assert.That(notes[0].Type).IsEqualTo("breaking");
        await Assert.That(notes[1].Type).IsEqualTo("warning");
        await Assert.That(notes[2].Type).IsEqualTo("info");
    }

    [Test]
    public async Task Parse_CustomHeading_BecomesSectionWithTitle()
    {
        var input = "### Features\n- New dashboard\n- Bluetooth pairing";
        var result = ChangelogParser.Parse(input);

        await Assert.That(result.IsT0).IsTrue();
        var notes = result.AsT0;
        await Assert.That(notes).HasCount(2);
        await Assert.That(notes[0].Type).IsEqualTo("section");
        await Assert.That(notes[0].Title).IsEqualTo("Features");
        await Assert.That(notes[0].Content).IsEqualTo("New dashboard");
        await Assert.That(notes[1].Title).IsEqualTo("Features");
        await Assert.That(notes[1].Content).IsEqualTo("Bluetooth pairing");
    }

    [Test]
    public async Task Parse_ExtractsBoldTitlePrefix_WithEmDashSeparator()
    {
        var input = "### Breaking\n**Config format** — Changed to TOML";
        var result = ChangelogParser.Parse(input);

        await Assert.That(result.IsT0).IsTrue();
        var notes = result.AsT0;
        await Assert.That(notes).HasCount(1);
        await Assert.That(notes[0].Type).IsEqualTo("breaking");
        await Assert.That(notes[0].Title).IsEqualTo("Config format");
        await Assert.That(notes[0].Content).IsEqualTo("Changed to TOML");
    }

    [Test]
    public async Task Parse_MultiLineItemWithoutBullets_IsCombined()
    {
        var input = "### Info\nLine one\nLine two";
        var result = ChangelogParser.Parse(input);

        await Assert.That(result.IsT0).IsTrue();
        var notes = result.AsT0;
        await Assert.That(notes).HasCount(1);
        await Assert.That(notes[0].Content).IsEqualTo("Line one\nLine two");
    }

    [Test]
    public async Task Parse_PlainItem_NoTitleExtracted()
    {
        var input = "### Info\n- Fixed WiFi reconnection";
        var result = ChangelogParser.Parse(input);

        await Assert.That(result.IsT0).IsTrue();
        var notes = result.AsT0;
        await Assert.That(notes).HasCount(1);
        await Assert.That(notes[0].Type).IsEqualTo("info");
        await Assert.That(notes[0].Title).IsNull();
        await Assert.That(notes[0].Content).IsEqualTo("Fixed WiFi reconnection");
    }

    [Test]
    public async Task Parse_SpecExample_MatchesGoldenStructure()
    {
        var input = string.Join("\n",
            "### Breaking",
            "**Config format** — Changed config format to TOML",
            "",
            "### Warning",
            "Requires hub reset after update",
            "",
            "### Info",
            "- Fixed WiFi reconnection",
            "- Improved battery life",
            "- **OTA** — Added automatic rollback on failed flash",
            "",
            "### Features",
            "- New web dashboard",
            "- Bluetooth pairing support");
        var result = ChangelogParser.Parse(input);

        await Assert.That(result.IsT0).IsTrue();
        var notes = result.AsT0;
        await Assert.That(notes).HasCount(7);

        await Assert.That(notes[0].Type).IsEqualTo("breaking");
        await Assert.That(notes[0].Title).IsEqualTo("Config format");
        await Assert.That(notes[0].Content).IsEqualTo("Changed config format to TOML");

        await Assert.That(notes[1].Type).IsEqualTo("warning");
        await Assert.That(notes[1].Content).IsEqualTo("Requires hub reset after update");

        await Assert.That(notes[2].Type).IsEqualTo("info");
        await Assert.That(notes[2].Content).IsEqualTo("Fixed WiFi reconnection");
        await Assert.That(notes[3].Content).IsEqualTo("Improved battery life");
        await Assert.That(notes[4].Title).IsEqualTo("OTA");
        await Assert.That(notes[4].Content).IsEqualTo("Added automatic rollback on failed flash");

        await Assert.That(notes[5].Type).IsEqualTo("section");
        await Assert.That(notes[5].Title).IsEqualTo("Features");
        await Assert.That(notes[5].Content).IsEqualTo("New web dashboard");
        await Assert.That(notes[6].Content).IsEqualTo("Bluetooth pairing support");
    }
}
