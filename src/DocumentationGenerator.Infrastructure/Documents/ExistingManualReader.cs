using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using DocumentationGenerator.Application.Contracts;
using DocumentationGenerator.Domain.Models;

namespace DocumentationGenerator.Infrastructure.Documents;

public sealed class ExistingManualReader : IExistingManualReader
{
    public Task<ExistingManual> ReadAsync(string path, string originalFileName,
        CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var document = WordprocessingDocument.Open(path, false);
        var mainPart = document.MainDocumentPart
            ?? throw new InvalidDataException("The Word document does not contain a main document part.");
        var body = mainPart.Document.Body
            ?? throw new InvalidDataException("The Word document does not contain a body.");
        var styleNames = BuildStyleMap(mainPart);
        var result = new ExistingManual { FileName = Path.GetFileName(originalFileName) };
        var plainText = new StringBuilder();
        var order = 0;

        foreach (var child in body.ChildElements)
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (child)
            {
                case Paragraph paragraph:
                {
                    var text = Normalize(paragraph.InnerText);
                    if (string.IsNullOrWhiteSpace(text)) break;
                    var styleId = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value ?? string.Empty;
                    var styleName = styleNames.GetValueOrDefault(styleId, styleId);
                    var isHeading = styleName.Contains("heading", StringComparison.OrdinalIgnoreCase) ||
                                    paragraph.ParagraphProperties?.OutlineLevel is not null;
                    result.Paragraphs.Add(new ManualParagraph
                    {
                        Order = order++,
                        Text = text,
                        StyleName = styleName,
                        IsHeading = isHeading
                    });
                    if (isHeading) result.Headings.Add(text);
                    plainText.AppendLine(text);
                    break;
                }
                case Table table:
                {
                    var manualTable = new ManualTable { Order = order++ };
                    foreach (var row in table.Elements<TableRow>())
                    {
                        var cells = row.Elements<TableCell>().Select(cell => Normalize(cell.InnerText)).ToList();
                        if (cells.Any(cell => !string.IsNullOrWhiteSpace(cell))) manualTable.Rows.Add(cells);
                    }
                    if (manualTable.Rows.Count == 0) break;
                    result.Tables.Add(manualTable);
                    foreach (var row in manualTable.Rows) plainText.AppendLine(string.Join(" | ", row));
                    break;
                }
            }
        }

        result.PlainText = plainText.ToString().Trim();
        result.DetectedStyleObservations = DetectStyle(result);
        return result;
    }, cancellationToken);

    private static Dictionary<string, string> BuildStyleMap(MainDocumentPart mainPart)
    {
        var styles = mainPart.StyleDefinitionsPart?.Styles?.Elements<Style>() ?? [];
        return styles.Where(style => style.StyleId is not null)
            .ToDictionary(style => style.StyleId!.Value!,
                style => style.StyleName?.Val?.Value ?? style.StyleId!.Value!,
                StringComparer.OrdinalIgnoreCase);
    }

    private static List<string> DetectStyle(ExistingManual manual)
    {
        var observations = new List<string>();
        if (manual.Headings.Any(heading => Regex.IsMatch(heading, @"^\d+(\.\d+)*[.)]?\s+")))
        {
            observations.Add("Uses numbered headings.");
        }
        if (manual.Tables.Any(table => table.Rows.FirstOrDefault()?.Any(cell =>
                Regex.IsMatch(cell, "feature|button|action", RegexOptions.IgnoreCase)) == true))
        {
            observations.Add("Documents features or buttons in tables.");
        }
        var narrative = manual.Paragraphs.Where(paragraph => !paragraph.IsHeading).Select(paragraph => paragraph.Text).ToList();
        if (narrative.Count > 0)
        {
            var averageWords = narrative.Average(text => text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);
            observations.Add(averageWords <= 18
                ? "Uses concise descriptions."
                : "Uses detailed explanatory descriptions.");
        }
        if (manual.Paragraphs.Any(paragraph => Regex.IsMatch(paragraph.Text, @"^(step(s)?|\d+[.)])", RegexOptions.IgnoreCase)))
        {
            observations.Add("Uses task-oriented procedural steps.");
        }
        if (manual.Headings.Count > 0)
        {
            observations.Add($"Common headings include: {string.Join(", ", manual.Headings.Take(8))}.");
        }
        return observations;
    }

    private static string Normalize(string value) => Regex.Replace(value ?? string.Empty, @"\s+", " ").Trim();
}
