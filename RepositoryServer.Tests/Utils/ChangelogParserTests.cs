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
        await Assert.That(notes).Count().IsEqualTo(3);
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
        await Assert.That(notes).Count().IsEqualTo(2);
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
        await Assert.That(notes).Count().IsEqualTo(1);
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
        await Assert.That(notes).Count().IsEqualTo(1);
        await Assert.That(notes[0].Content).IsEqualTo("Line one\nLine two");
    }

    [Test]
    public async Task Parse_PlainItem_NoTitleExtracted()
    {
        var input = "### Info\n- Fixed WiFi reconnection";
        var result = ChangelogParser.Parse(input);

        await Assert.That(result.IsT0).IsTrue();
        var notes = result.AsT0;
        await Assert.That(notes).Count().IsEqualTo(1);
        await Assert.That(notes[0].Type).IsEqualTo("info");
        await Assert.That(notes[0].Title).IsNull();
        await Assert.That(notes[0].Content).IsEqualTo("Fixed WiFi reconnection");
    }

    [Test]
    [Arguments("### Info\n**Title** — content", "Title", "content")]
    [Arguments("### Info\n**Title** – content", "Title", "content")]
    [Arguments("### Info\n**Title** - content", "Title", "content")]
    [Arguments("### Info\n**Title** -content", "Title", "content")]
    public async Task Parse_SupportsMultipleTitleSeparators(string input, string expectedTitle, string expectedContent)
    {
        var result = ChangelogParser.Parse(input);
        await Assert.That(result.IsT0).IsTrue();
        var notes = result.AsT0;
        await Assert.That(notes).Count().IsEqualTo(1);
        await Assert.That(notes[0].Title).IsEqualTo(expectedTitle);
        await Assert.That(notes[0].Content).IsEqualTo(expectedContent);
    }

    [Test]
    public async Task Parse_IgnoresLinesBeforeFirstHeading()
    {
        var input = "Intro text we should ignore\n\n### Info\nActual content";
        var result = ChangelogParser.Parse(input);

        await Assert.That(result.IsT0).IsTrue();
        var notes = result.AsT0;
        await Assert.That(notes).Count().IsEqualTo(1);
        await Assert.That(notes[0].Content).IsEqualTo("Actual content");
    }

    [Test]
    public async Task Parse_HandlesCrlfLineEndings()
    {
        var input = "### Info\r\n- Fixed WiFi\r\n### Warning\r\nNeeds reset";
        var result = ChangelogParser.Parse(input);

        await Assert.That(result.IsT0).IsTrue();
        var notes = result.AsT0;
        await Assert.That(notes).Count().IsEqualTo(2);
        await Assert.That(notes[0].Type).IsEqualTo("info");
        await Assert.That(notes[0].Content).IsEqualTo("Fixed WiFi");
        await Assert.That(notes[1].Type).IsEqualTo("warning");
        await Assert.That(notes[1].Content).IsEqualTo("Needs reset");
    }

    [Test]
    public async Task Parse_HeadingsAreCaseInsensitive()
    {
        var input = "### BREAKING\nFoo\n### warning\nBar\n### Info\nBaz";
        var result = ChangelogParser.Parse(input);

        await Assert.That(result.IsT0).IsTrue();
        var notes = result.AsT0;
        await Assert.That(notes).Count().IsEqualTo(3);
        await Assert.That(notes[0].Type).IsEqualTo("breaking");
        await Assert.That(notes[1].Type).IsEqualTo("warning");
        await Assert.That(notes[2].Type).IsEqualTo("info");
    }

    [Test]
    public async Task Parse_SectionProseAndBulletsBothBecomeNotes()
    {
        // Prose alongside bullets is content, not noise — it used to be silently discarded.
        var input = "### Info\nIntroductory prose\n- Actual item 1\n- Actual item 2";
        var result = ChangelogParser.Parse(input);

        await Assert.That(result.IsT0).IsTrue();
        var notes = result.AsT0;
        await Assert.That(notes).Count().IsEqualTo(3);
        await Assert.That(notes[0].Content).IsEqualTo("Introductory prose");
        await Assert.That(notes[1].Content).IsEqualTo("Actual item 1");
        await Assert.That(notes[2].Content).IsEqualTo("Actual item 2");
    }

    [Test]
    public async Task Parse_NestedBulletsAreFlattenedNotDropped()
    {
        // The DTO has no nesting, so indented items become siblings. Previously they fell into the
        // prose buffer and were discarded along with it.
        var input = "### Info\n- Parent item\n  - Nested item\n    - Deeply nested item";
        var result = ChangelogParser.Parse(input);

        await Assert.That(result.IsT0).IsTrue();
        var notes = result.AsT0;
        await Assert.That(notes).Count().IsEqualTo(3);
        await Assert.That(notes[0].Content).IsEqualTo("Parent item");
        await Assert.That(notes[1].Content).IsEqualTo("Nested item");
        await Assert.That(notes[2].Content).IsEqualTo("Deeply nested item");
    }

    [Test]
    public async Task Parse_EmptyBoldTitle_FallsBackToLiteralContent()
    {
        var input = "### Info\n**** — still content";
        var result = ChangelogParser.Parse(input);

        await Assert.That(result.IsT0).IsTrue();
        var notes = result.AsT0;
        await Assert.That(notes).Count().IsEqualTo(1);
        await Assert.That(notes[0].Title).IsNull();
        await Assert.That(notes[0].Content).IsEqualTo("**** — still content");
    }

    [Test]
    public async Task Parse_SpecExample_MatchesGoldenStructure()
    {
        // Reproduced verbatim from the §5.3 worked example. Do not trim lines out of this fixture to
        // make it pass — every line exercises a documented construct, and a shorter fixture is how the
        // section-prose divergence went unnoticed.
        var input = string.Join("\n",
            "### Breaking",
            "**Config format** — Changed config format to TOML",
            "Multiple lines of content are concatenated into a single note.",
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
            "Custom sections become type \"section\" with the heading as title.",
            "- New web dashboard",
            "- Bluetooth pairing support");
        var result = ChangelogParser.Parse(input);

        await Assert.That(result.IsT0).IsTrue();
        var notes = result.AsT0;
        await Assert.That(notes).Count().IsEqualTo(8);

        await Assert.That(notes[0].Type).IsEqualTo("breaking");
        await Assert.That(notes[0].Title).IsEqualTo("Config format");
        await Assert.That(notes[0].Content)
            .IsEqualTo("Changed config format to TOML\nMultiple lines of content are concatenated into a single note.");

        await Assert.That(notes[1].Type).IsEqualTo("warning");
        await Assert.That(notes[1].Content).IsEqualTo("Requires hub reset after update");

        await Assert.That(notes[2].Type).IsEqualTo("info");
        await Assert.That(notes[2].Content).IsEqualTo("Fixed WiFi reconnection");
        await Assert.That(notes[3].Content).IsEqualTo("Improved battery life");
        await Assert.That(notes[4].Title).IsEqualTo("OTA");
        await Assert.That(notes[4].Content).IsEqualTo("Added automatic rollback on failed flash");

        // Section prose is a note in its own right, ahead of the section's bullets.
        await Assert.That(notes[5].Type).IsEqualTo("section");
        await Assert.That(notes[5].Title).IsEqualTo("Features");
        await Assert.That(notes[5].Content)
            .IsEqualTo("Custom sections become type \"section\" with the heading as title.");

        await Assert.That(notes[6].Type).IsEqualTo("section");
        await Assert.That(notes[6].Title).IsEqualTo("Features");
        await Assert.That(notes[6].Content).IsEqualTo("New web dashboard");
        await Assert.That(notes[7].Content).IsEqualTo("Bluetooth pairing support");
    }
}
