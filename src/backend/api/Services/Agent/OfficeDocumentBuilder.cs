using System.Globalization;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace Chatbot.Api.Services.Agent;

public sealed record SheetData(string Name, IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string?>> Rows);

/// <summary>Builds Office documents from model output: DOCX from a Markdown subset, XLSX from tabular data.</summary>
public static partial class OfficeDocumentBuilder
{
    public const string DocxContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    public const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public const string PdfContentType = "application/pdf";

    /// <summary>Supports headings, paragraphs, bullet/numbered lists, pipe tables and **bold**, *italic*, `code`.</summary>
    public static byte[] CreateDocx(string title, string markdown)
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = document.AddMainDocumentPart();
            AddWordStyles(main);
            var body = new W.Body();
            if (!string.IsNullOrWhiteSpace(title))
            {
                body.Append(StyledParagraph("Title", title));
            }

            var lines = markdown.Replace("\r\n", "\n").Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].TrimEnd();
                if (line.Trim().Length == 0)
                {
                    continue;
                }

                if (line.TrimStart().StartsWith('|'))
                {
                    var tableLines = new List<string>();
                    while (i < lines.Length && lines[i].TrimStart().StartsWith('|'))
                    {
                        tableLines.Add(lines[i].Trim());
                        i++;
                    }

                    i--;
                    body.Append(CreateTable(tableLines));
                    body.Append(new W.Paragraph());
                    continue;
                }

                var heading = HeadingRegex().Match(line);
                if (heading.Success)
                {
                    body.Append(StyledParagraph($"Heading{Math.Min(heading.Groups[1].Length, 3)}", heading.Groups[2].Value));
                    continue;
                }

                var bullet = BulletRegex().Match(line);
                if (bullet.Success)
                {
                    var level = bullet.Groups[1].Value.Length / 2;
                    body.Append(ListParagraph("•", bullet.Groups[2].Value, level));
                    continue;
                }

                var numbered = NumberedRegex().Match(line);
                if (numbered.Success)
                {
                    var level = numbered.Groups[1].Value.Length / 2;
                    body.Append(ListParagraph(numbered.Groups[2].Value + ".", numbered.Groups[3].Value, level));
                    continue;
                }

                var paragraph = new W.Paragraph();
                AppendInline(paragraph, line.Trim());
                body.Append(paragraph);
            }

            body.Append(new W.SectionProperties());
            main.Document = new W.Document(body);
        }

        return stream.ToArray();
    }

    public static byte[] CreateXlsx(IReadOnlyList<SheetData> sheets)
    {
        if (sheets.Count == 0)
        {
            throw new ArgumentException("At least one sheet is required.", nameof(sheets));
        }

        using var stream = new MemoryStream();
        using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var workbook = document.AddWorkbookPart();
            workbook.Workbook = new Workbook();
            var styles = workbook.AddNewPart<WorkbookStylesPart>();
            styles.Stylesheet = new Stylesheet(
                new Fonts(new Font(), new Font(new Bold())) { Count = 2 },
                new Fills(new Fill(new PatternFill { PatternType = PatternValues.None }),
                    new Fill(new PatternFill { PatternType = PatternValues.Gray125 })) { Count = 2 },
                new Borders(new Border()) { Count = 1 },
                new CellFormats(new CellFormat(), new CellFormat { FontId = 1, ApplyFont = true }) { Count = 2 });

            var sheetList = workbook.Workbook.AppendChild(new Sheets());
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            uint sheetId = 1;
            foreach (var sheet in sheets)
            {
                var part = workbook.AddNewPart<WorksheetPart>();
                var data = new DocumentFormat.OpenXml.Spreadsheet.SheetData();
                data.Append(CreateRow(sheet.Columns, bold: true));
                foreach (var row in sheet.Rows)
                {
                    data.Append(CreateRow(row, bold: false));
                }

                part.Worksheet = new Worksheet(data);
                sheetList.Append(new Sheet
                {
                    Id = workbook.GetIdOfPart(part),
                    SheetId = sheetId,
                    Name = UniqueSheetName(sheet.Name, sheetId, usedNames)
                });
                sheetId++;
            }
        }

        return stream.ToArray();
    }

    private static Row CreateRow(IEnumerable<string?> values, bool bold)
    {
        var row = new Row();
        foreach (var value in values)
        {
            var text = value ?? "";
            var cell = new Cell { DataType = CellValues.InlineString,
                InlineString = new InlineString(new Text(text) { Space = SpaceProcessingModeValues.Preserve }) };
            if (bold)
            {
                cell.StyleIndex = 1;
            }

            row.Append(cell);
        }

        return row;
    }

    private static string UniqueSheetName(string name, uint index, HashSet<string> used)
    {
        var clean = InvalidSheetCharsRegex().Replace(name ?? "", "").Trim('\'', ' ');
        clean = clean.Length == 0 ? $"Sheet{index}" : clean[..Math.Min(clean.Length, 31)];
        var candidate = clean;
        for (var n = 2; !used.Add(candidate); n++)
        {
            var suffix = $" ({n})";
            candidate = clean[..Math.Min(clean.Length, 31 - suffix.Length)] + suffix;
        }

        return candidate;
    }

    private static W.Table CreateTable(List<string> lines)
    {
        var rows = lines.Where(line => !TableSeparatorRegex().IsMatch(line))
            .Select(line => line.Trim('|').Split('|').Select(cell => cell.Trim()).ToList()).ToList();
        var border = new W.TableBorders(
            new W.TopBorder { Val = W.BorderValues.Single, Size = 4 },
            new W.BottomBorder { Val = W.BorderValues.Single, Size = 4 },
            new W.LeftBorder { Val = W.BorderValues.Single, Size = 4 },
            new W.RightBorder { Val = W.BorderValues.Single, Size = 4 },
            new W.InsideHorizontalBorder { Val = W.BorderValues.Single, Size = 4 },
            new W.InsideVerticalBorder { Val = W.BorderValues.Single, Size = 4 });
        var table = new W.Table(new W.TableProperties(border,
            new W.TableWidth { Type = W.TableWidthUnitValues.Pct, Width = "5000" }));
        for (var r = 0; r < rows.Count; r++)
        {
            var tableRow = new W.TableRow();
            foreach (var cellText in rows[r])
            {
                var paragraph = new W.Paragraph();
                AppendInline(paragraph, r == 0 ? $"**{cellText.Trim('*')}**" : cellText);
                tableRow.Append(new W.TableCell(paragraph));
            }

            table.Append(tableRow);
        }

        return table;
    }

    private static W.Paragraph StyledParagraph(string styleId, string text)
    {
        var paragraph = new W.Paragraph(new W.ParagraphProperties(new W.ParagraphStyleId { Val = styleId }));
        AppendInline(paragraph, text.Trim());
        return paragraph;
    }

    private static W.Paragraph ListParagraph(string marker, string text, int level)
    {
        var indent = 360 + level * 360;
        var paragraph = new W.Paragraph(new W.ParagraphProperties(
            new W.Indentation { Left = indent.ToString(CultureInfo.InvariantCulture), Hanging = "360" }));
        paragraph.Append(new W.Run(new W.Text(marker + "\t") { Space = SpaceProcessingModeValues.Preserve }));
        AppendInline(paragraph, text.Trim());
        return paragraph;
    }

    private static void AppendInline(W.Paragraph paragraph, string text)
    {
        text = LinkRegex().Replace(text, "$1 ($2)");
        var position = 0;
        foreach (Match match in InlineRegex().Matches(text))
        {
            if (match.Index > position)
            {
                paragraph.Append(TextRun(text[position..match.Index], null));
            }

            var properties = new W.RunProperties();
            if (match.Groups["bold"].Success)
            {
                properties.Append(new W.Bold());
                paragraph.Append(TextRun(match.Groups["bold"].Value, properties));
            }
            else if (match.Groups["italic"].Success)
            {
                properties.Append(new W.Italic());
                paragraph.Append(TextRun(match.Groups["italic"].Value, properties));
            }
            else
            {
                properties.Append(new W.RunFonts { Ascii = "Consolas", HighAnsi = "Consolas" });
                paragraph.Append(TextRun(match.Groups["code"].Value, properties));
            }

            position = match.Index + match.Length;
        }

        if (position < text.Length)
        {
            paragraph.Append(TextRun(text[position..], null));
        }
    }

    private static W.Run TextRun(string text, W.RunProperties? properties)
    {
        var run = new W.Run();
        if (properties is not null)
        {
            run.Append(properties);
        }

        run.Append(new W.Text(text) { Space = SpaceProcessingModeValues.Preserve });
        return run;
    }

    private static void AddWordStyles(MainDocumentPart main)
    {
        static W.Style Heading(string id, string name, string size, string? color = null)
        {
            var runProperties = new W.StyleRunProperties(new W.Bold(), new W.FontSize { Val = size });
            if (color is not null)
            {
                runProperties.Append(new W.Color { Val = color });
            }

            return new W.Style(
                new W.StyleName { Val = name },
                new W.BasedOn { Val = "Normal" },
                new W.NextParagraphStyle { Val = "Normal" },
                new W.PrimaryStyle(),
                new W.StyleParagraphProperties(new W.KeepNext(), new W.SpacingBetweenLines { Before = "240", After = "120" }),
                runProperties)
            { Type = W.StyleValues.Paragraph, StyleId = id };
        }

        var styles = main.AddNewPart<StyleDefinitionsPart>();
        styles.Styles = new W.Styles(
            new W.DocDefaults(
                new W.RunPropertiesDefault(new W.RunPropertiesBaseStyle(
                    new W.RunFonts { Ascii = "Aptos", HighAnsi = "Aptos", ComplexScript = "Aptos" },
                    new W.FontSize { Val = "22" })),
                new W.ParagraphPropertiesDefault(new W.ParagraphPropertiesBaseStyle(
                    new W.SpacingBetweenLines { After = "120", Line = "276", LineRule = W.LineSpacingRuleValues.Auto }))),
            new W.Style(new W.StyleName { Val = "Normal" }, new W.PrimaryStyle())
            { Type = W.StyleValues.Paragraph, StyleId = "Normal", Default = true },
            Heading("Title", "Title", "48", "1F3864"),
            Heading("Heading1", "heading 1", "32", "2F5496"),
            Heading("Heading2", "heading 2", "26", "2F5496"),
            Heading("Heading3", "heading 3", "24", "1F3763"));
    }

    [GeneratedRegex(@"^\s{0,3}(#{1,6})\s+(.+)$")]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(@"^(\s*)[-*+]\s+(.+)$")]
    private static partial Regex BulletRegex();

    [GeneratedRegex(@"^(\s*)(\d+)[.)]\s+(.+)$")]
    private static partial Regex NumberedRegex();

    [GeneratedRegex(@"^\|?\s*:?-{2,}:?\s*(\|\s*:?-{2,}:?\s*)*\|?$")]
    private static partial Regex TableSeparatorRegex();

    [GeneratedRegex(@"\*\*(?<bold>.+?)\*\*|(?<![\w*])\*(?<italic>[^*\s][^*]*?)\*(?![\w*])|`(?<code>[^`]+)`")]
    private static partial Regex InlineRegex();

    [GeneratedRegex(@"\[([^\]]+)\]\((https?://[^)\s]+)\)")]
    private static partial Regex LinkRegex();

    [GeneratedRegex(@"[\[\]:*?/\\]")]
    private static partial Regex InvalidSheetCharsRegex();
}
