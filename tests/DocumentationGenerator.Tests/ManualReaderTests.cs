using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using DocumentationGenerator.Infrastructure.Documents;

namespace DocumentationGenerator.Tests;

public sealed class ManualReaderTests
{
    [Fact]
    public async Task Reads_Paragraphs_Headings_Tables_And_Style_Observations()
    {
        var path = Path.Combine(Path.GetTempPath(), $"manual-{Guid.NewGuid():N}.docx");
        try
        {
            using (var document = WordprocessingDocument.Create(path, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
            {
                var main = document.AddMainDocumentPart();
                main.Document = new Document(new Body(
                    new Paragraph(new ParagraphProperties(new ParagraphStyleId { Val = "Heading1" }), new Run(new Text("1. Customer Management"))),
                    new Paragraph(new Run(new Text("Use Search to find customer records."))),
                    new Table(new TableRow(new TableCell(new Paragraph(new Run(new Text("Feature")))), new TableCell(new Paragraph(new Run(new Text("Description"))))),
                        new TableRow(new TableCell(new Paragraph(new Run(new Text("Search")))), new TableCell(new Paragraph(new Run(new Text("Find records"))))))));
                var styles = main.AddNewPart<StyleDefinitionsPart>();
                styles.Styles = new Styles(new Style(new StyleName { Val = "Heading 1" }) { StyleId = "Heading1", Type = StyleValues.Paragraph });
                main.Document.Save();
            }

            var result = await new ExistingManualReader().ReadAsync(path, "manual.docx");

            Assert.Contains("Customer Management", result.PlainText);
            Assert.Contains(result.Headings, heading => heading.StartsWith("1."));
            Assert.Single(result.Tables);
            Assert.Contains(result.DetectedStyleObservations, item => item.Contains("numbered", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.DetectedStyleObservations, item => item.Contains("tables", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
