using System.IO.Compression;
using System.Text.Json;
using System.Text;
using Azure.Core;
using Chatbot.Api.Services;
using Chatbot.Api.Services.Agent;
using Chatbot.Api.Services.Ingestion;
using Chatbot.Api.Services.Storage;
using DocumentFormat.OpenXml.Packaging;
using Xunit;

namespace Chatbot.Api.Tests;

public sealed class BuildingBlockTests
{
    [Fact]
    public void ChunkerSplitsOnSectionsAndKeepsHeadingContext()
    {
        var paragraph = string.Join(' ', Enumerable.Repeat("Housing policy sentence.", 20));
        var markdown = $"# Report\r\n\r\nIntro text.\r\n\r\n<!-- PageBreak -->\r\n\r\n## Budget\r\n\r\n{paragraph}\r\n\r\n{paragraph}\r\n\r\n{paragraph}";

        var chunks = MarkdownChunker.Chunk(markdown, maxChars: 1000, overlapChars: 100);

        Assert.Equal("# Report\n\nIntro text.", chunks[0]);
        Assert.StartsWith("## Budget", chunks[1]);
        Assert.True(chunks.Count >= 3);
        Assert.StartsWith("Report > Budget\n\n…", chunks[2]);
        Assert.All(chunks, chunk => Assert.DoesNotContain("PageBreak", chunk));
    }

    [Fact]
    public void ChunkerSplitsBlocksLargerThanTheLimit()
    {
        var chunks = MarkdownChunker.Chunk(new string('x', 4500), maxChars: 2000, overlapChars: 0);
        Assert.Equal([2000, 2000, 500], chunks.Select(chunk => chunk.Length));
    }

    [Fact]
    public void ChunkMetadataPreservesSectionsAndPagesWithoutCrossBoundaryOverlap()
    {
        var chunks = MarkdownChunker.ChunkWithMetadata(
            "<!-- SourcePage: 1 -->\n\n# Report\n\nIntroduction.\n\n## Housing\n\nRent policy.\n\n" +
            "### Exceptions\n\nExceptions apply.\n\n<!-- SourcePage: 2 -->\n\nFurther exceptions.", overlapChars: 100);
        Assert.Equal(4, chunks.Count);
        Assert.Equal("Report > Housing", chunks[1].Section);
        Assert.Equal(["Report", "Report > Housing", "Report > Housing > Exceptions"], chunks[2].SectionPaths);
        Assert.Equal(2, chunks[3].PageNumber);
        Assert.DoesNotContain("Rent policy", chunks[2].Content);
        Assert.DoesNotContain("Exceptions apply", chunks[3].Content);
        Assert.All(chunks, chunk => Assert.DoesNotContain("SourcePage", chunk.Content));
        Assert.Null(Assert.Single(MarkdownChunker.ChunkWithMetadata("Plain text without page metadata.")).PageNumber);
    }

    [Fact]
    public void SearchFiltersFailClosedAndEscapeUserAndSectionValues()
    {
        Assert.Equal("ownerId eq 'user' and false", KnowledgeIndex.ScopeFilter("user", []));
        Assert.Equal("ownerId eq 'o''id' and (documentId eq 'doc''1') and sectionPaths/any(s: s eq 'Report > Owner''s notes') and pageNumber eq 3",
            KnowledgeIndex.ScopeFilter("o'id", ["doc'1"], "Report > Owner's notes", 3));
    }

    [Fact]
    public void InvalidRetrievalPlansCannotBroadenDocumentOrSectionScope()
    {
        Assert.Throws<InvalidOperationException>(() => DocumentRetrieval.ValidatePlan(
            new("query", "document", ["other-user-doc"], null, null, null), ["doc1"], []));
        Assert.Throws<InvalidOperationException>(() => DocumentRetrieval.ValidatePlan(
            new("query", "section", ["doc1"], "Invented", null, null), ["doc1"], ["Budget"]));
        Assert.Throws<InvalidOperationException>(() => DocumentRetrieval.ValidatePlan(
            new("query", "section", [], null, null, null), ["doc1"], ["Budget"]));
    }

    [Theory]
    [InlineData("removed")]
    [InlineData("added")]
    [InlineData("deleted")]
    [InlineData("processing")]
    [InlineData("updated")]
    [InlineData("legacy")]
    [InlineData("event")]
    [InlineData("interrupted")]
    [InlineData("uncited")]
    [InlineData("foreign-citation")]
    public void ReuseRejectsChangedDocumentContextAndUnverifiableEvidence(string change)
    {
        var document = new UserDocument
        {
            Id = "doc1", UserId = "user", FileName = "Report", SourceType = "upload",
            Status = DocumentStatus.Ready, UpdatedAt = DateTimeOffset.UtcNow
        };
        var prior = new ChatMessage("answer", ChatMessage.AssistantRole, "Prior answer [2]", DateTimeOffset.UtcNow)
        {
            Citations = [new(2, "chunk", "doc1", "Report", "upload", null, 0, null, null, "Supporting evidence")],
            Retrieval = new("question", "thread", ["doc1"], null, null, 1)
            {
                AttachedDocumentIds = ["doc1"], DocumentVersions = [new("doc1", document.UpdatedAt)]
            }
        };
        var turn = new ChatTurn("user", [prior], "Explain that fact.") { DocumentIds = ["doc1"] };
        IReadOnlyList<UserDocument> ready = [document];
        switch (change)
        {
            case "removed": turn = turn with { DocumentIds = [] }; break;
            case "added": turn = turn with { DocumentIds = ["doc1", "doc2"] }; break;
            case "deleted":
            case "processing": ready = []; break;
            case "updated": ready = [document with { UpdatedAt = document.UpdatedAt.AddSeconds(1) }]; break;
            case "legacy": turn = turn with { History = [prior with { Retrieval = prior.Retrieval with { DocumentVersions = [] } }] }; break;
            case "event": turn = turn with { History = [prior, new("event", "event", "Removed and re-added doc1", DateTimeOffset.UtcNow)] }; break;
            case "interrupted": turn = turn with { History = [prior, new("user", "user", "Unfinished turn", DateTimeOffset.UtcNow)] }; break;
            case "uncited": turn = turn with { History = [prior with { Citations = [] }] }; break;
            case "foreign-citation": turn = turn with { History = [prior with { Citations = [prior.Citations[0] with { DocumentId = "foreign" }] }] }; break;
        }
        Assert.Empty(DocumentRetrieval.ReusablePassages(turn, ready));
    }

    [Fact]
    public void ReuseRenumbersOnlyValidatedSavedPassages()
    {
        var document = new UserDocument { Id = "doc1", UserId = "user", FileName = "Report", SourceType = "upload", Status = DocumentStatus.Ready };
        var citation = new PassageCitation(7, "chunk", "doc1", "Report", "upload", null, 4, "Budget", 3, "Saved quote");
        var prior = new ChatMessage("answer", "assistant", "Claim [7].", DateTimeOffset.UtcNow)
        {
            Citations = [citation],
            Retrieval = new("query", "section", ["doc1"], "Budget", 3, 1)
            {
                AttachedDocumentIds = ["doc1"], DocumentVersions = [new("doc1", document.UpdatedAt)]
            }
        };
        var sources = DocumentRetrieval.ReusablePassages(new("user", [prior], "Explain that.") { DocumentIds = ["doc1"] }, [document]);
        Assert.Equal(citation with { Number = 1, RetrievalMethod = "reuse" }, Assert.Single(sources));
    }

    [Theory]
    [InlineData("""{"followUpQuestions":[]}""")]
    [InlineData("""{"followUpQuestions":[""]}""")]
    [InlineData("""{"followUpQuestions":["a","b","c","d"]}""")]
    [InlineData("""{"followUpQuestions":[null]}""")]
    [InlineData("""{"followUpQuestions":[12]}""")]
    [InlineData("""{}""")]
    public void InvalidSuggestedQuestionsAreRejected(string json) =>
        Assert.Throws<InvalidOperationException>(() => SuggestedQuestions.Parse(JsonDocument.Parse(json).RootElement));

    [Fact]
    public void SuggestedQuestionsTrimAndDeduplicate()
    {
        Assert.Equal(["What next?"], SuggestedQuestions.Parse(JsonDocument.Parse("""{"followUpQuestions":[" What next? ","what NEXT?"]}""").RootElement));
        Assert.Throws<InvalidOperationException>(() => SuggestedQuestions.Parse(JsonDocument.Parse(
            JsonSerializer.Serialize(new { followUpQuestions = new[] { new string('a', 241) } })).RootElement));
    }

    [Fact]
    public void StructuredAnswerStreamsEscapedTextAndUnicodeAcrossFragments()
    {
        const string answer = "Quote: \"yes\"; path: C:\\docs\nAccent: \u00e9; emoji: \U0001F680";
        var json = JsonSerializer.Serialize(new { answer, followUpQuestions = new[] { "What next?" }, offerFollowUps = true });
        var response = new StreamedAnswer();
        var streamed = new StringBuilder();
        foreach (var character in json) streamed.Append(response.Append(character.ToString()));

        Assert.Equal(answer, streamed.ToString());
        var completed = response.Complete();
        Assert.Equal("", completed.Remaining);
        Assert.Equal(["What next?"], completed.Questions);
    }

    [Fact]
    public void StructuredAnswerHandlesAReorderedObjectWithoutLeakingJson()
    {
        var response = new StreamedAnswer();
        var text = response.Append("""{"offerFollowUps":true,"followUpQuestions":["Why?"],"answer":"An answer."}""");
        Assert.Empty(text);
        var completed = response.Complete();
        Assert.Equal("An answer.", completed.Remaining);
        Assert.Equal(["Why?"], completed.Questions);
    }

    [Fact]
    public void CitationsArePassageSpecificAndUnknownReferencesHaveNoEvidence()
    {
        var sources = new[]
        {
            new PassageCitation(1, "doc-c0", "doc", "Report", "upload", null, 0, "Budget", null, "First excerpt"),
            new PassageCitation(2, "doc-c1", "doc", "Report", "upload", null, 1, "Budget", null, "Second excerpt")
        };
        var cited = AgentChatService.CitedPassages("Claim [2]. Repeated [2]. Unknown [99].", sources);
        Assert.Equal("doc-c1", Assert.Single(cited).ChunkId);
        Assert.Equal("Second excerpt", cited[0].Quote);
    }

    [Fact]
    public void SkillParsesFrontMatterAndInstructions()
    {
        var skill = SkillCatalog.Parse("my-skill",
            "---\r\nname: my-skill\r\ndescription: \"Does things.\"\r\n---\r\n\r\n# Steps\r\nDo it.\r\n", new Dictionary<string, string>());
        Assert.Equal("Does things.", skill.Description);
        Assert.Equal("# Steps\nDo it.", skill.Instructions);
    }

    [Theory]
    [InlineData("other", "---\nname: my-skill\ndescription: x\n---\nBody")]
    [InlineData("My_Skill", "---\nname: My_Skill\ndescription: x\n---\nBody")]
    [InlineData("my-skill", "---\nname: my-skill\n---\nBody")]
    [InlineData("my-skill", "name: my-skill\ndescription: x")]
    public void InvalidSkillsAreRejected(string folder, string content) =>
        Assert.Throws<FormatException>(() => SkillCatalog.Parse(folder, content, new Dictionary<string, string>()));

    [Fact]
    public void BundledSkillsLoadWithTheirResources()
    {
        var catalog = new SkillCatalog(Path.Combine(AppContext.BaseDirectory, "skills"),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SkillCatalog>.Instance);
        var skill = Assert.IsType<Skill>(catalog.Find("extract-and-generate"));
        Assert.NotNull(catalog.ReadResource(skill, "references/output-guidelines.md"));
        Assert.Null(catalog.ReadResource(skill, "../SKILL.md"));
    }

    [Fact]
    public void DocxContainsHeadingsListsAndTables()
    {
        var bytes = OfficeDocumentBuilder.CreateDocx("Overview",
            "## Key facts\n\nSome **bold** text.\n\n- First\n- Second\n\n| Item | Value |\n|---|---|\n| Rent | 1200 |");

        using var document = WordprocessingDocument.Open(new MemoryStream(bytes), false);
        var body = document.MainDocumentPart!.Document!.Body!;
        var text = body.InnerText;
        Assert.Contains("Overview", text);
        Assert.Contains("Key facts", text);
        Assert.Contains("Second", text);
        Assert.Contains("1200", text);
        Assert.DoesNotContain("**", text);
        Assert.DoesNotContain("|", text);
        Assert.Single(body.Elements<DocumentFormat.OpenXml.Wordprocessing.Table>());
    }

    [Fact]
    public void XlsxContainsOneWorksheetPerSheet()
    {
        var bytes = OfficeDocumentBuilder.CreateXlsx([
            new SheetData("Data", ["Name", "Amount"], [["Rent", "1200"], ["Tax", null]]),
            new SheetData("Sources", ["Document"], [["notes.txt"]])
        ]);

        using var document = SpreadsheetDocument.Open(new MemoryStream(bytes), false);
        var names = document.WorkbookPart!.Workbook!.Sheets!.Elements<DocumentFormat.OpenXml.Spreadsheet.Sheet>()
            .Select(sheet => sheet.Name!.Value);
        Assert.Equal(["Data", "Sources"], names);
        using var zip = new ZipArchive(new MemoryStream(bytes));
        Assert.Contains(zip.Entries, entry => entry.FullName.StartsWith("xl/worksheets/", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("001234")]
    [InlineData("1234567890123456789")]
    [InlineData("1E10")]
    [InlineData("12.50")]
    [InlineData("=SUM(A1:A2)")]
    [InlineData("  spaced  ")]
    [InlineData("")]
    [InlineData(null)]
    public void XlsxPreservesCellsAsLiteralText(string? value)
    {
        var bytes = OfficeDocumentBuilder.CreateXlsx([new SheetData("Data", ["Value"], [[value]])]);
        using var document = SpreadsheetDocument.Open(new MemoryStream(bytes), false);
        var cell = document.WorkbookPart!.WorksheetParts.Single().Worksheet!
            .Descendants<DocumentFormat.OpenXml.Spreadsheet.Row>().Last()
            .Elements<DocumentFormat.OpenXml.Spreadsheet.Cell>().Single();
        Assert.Equal(DocumentFormat.OpenXml.Spreadsheet.CellValues.InlineString, cell.DataType!.Value);
        Assert.Equal(value ?? "", cell.InnerText);
        Assert.Null(cell.CellFormula);
    }

    [Theory]
    [InlineData("Report: Q1/2025", "docx", "Report Q12025.docx")]
    [InlineData("summary.docx", "pdf", "summary.pdf")]
    [InlineData("Plan v1.2", "docx", "Plan v1.2.docx")]
    [InlineData("  ", "xlsx", "document.xlsx")]
    public void GeneratedFileNamesAreSanitized(string input, string format, string expected) =>
        Assert.Equal(expected, CreateDocumentTool.SanitizeFileName(input, format));

    [Fact]
    public async Task CachedTokenCredentialReusesTokensUntilNearExpiry()
    {
        var time = new ManualTime(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var inner = new CountingCredential(time);
        var credential = new CachedTokenCredential(inner, time);
        var context = new TokenRequestContext(["https://ai.azure.com/.default"]);

        var first = await credential.GetTokenAsync(context, default);
        Assert.Equal(first.Token, (await credential.GetTokenAsync(context, default)).Token);
        Assert.Equal(1, inner.Calls);

        await credential.GetTokenAsync(new TokenRequestContext(["https://search.azure.com/.default"]), default);
        Assert.Equal(2, inner.Calls);

        await credential.GetTokenAsync(new TokenRequestContext(context.Scopes, claims: "{\"access_token\":{}}"), default);
        Assert.Equal(3, inner.Calls);

        time.Now += TimeSpan.FromMinutes(56);
        Assert.NotEqual(first.Token, (await credential.GetTokenAsync(context, default)).Token);
        Assert.Equal(4, inner.Calls);
    }

    private sealed class ManualTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class CountingCredential(TimeProvider time) : TokenCredential
    {
        public int Calls { get; private set; }

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new($"token-{++Calls}", time.GetUtcNow().AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            ValueTask.FromResult(GetToken(requestContext, cancellationToken));
    }
}
