using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;
using DocumentationGenerator.Application.Configuration;
using DocumentationGenerator.Application.Contracts;
using DocumentationGenerator.Application.Services;
using DocumentationGenerator.Domain.Models;
using DocumentationGenerator.Infrastructure.Export;
using PdfSharp.Pdf.IO;

namespace DocumentationGenerator.Tests;

public sealed class ExportAndValidationTests
{
    [Fact]
    public async Task Markdown_Export_Is_Human_Readable()
    {
        var path = TempPath(".md");
        try
        {
            await new MarkdownExporter().ExportAsync(Manual(), path);
            var content = await File.ReadAllTextAsync(path);
            Assert.Contains("# Customer Management User Manual", content);
            Assert.Contains("## Feature index", content);
            Assert.Contains("| 1 | Search |", content);
            Assert.DoesNotContain("{\"title\"", content);
        }
        finally { Delete(path); }
    }

    [Fact]
    public async Task Word_Export_Creates_A_Valid_Document()
    {
        var path = TempPath(".docx");
        try
        {
            await new WordExporter().ExportAsync(Manual(), path);
            using var document = WordprocessingDocument.Open(path, false);
            Assert.Contains("Feature index", document.MainDocumentPart!.Document.Body!.InnerText);
            Assert.Single(document.MainDocumentPart.FooterParts);

            var section = Assert.Single(document.MainDocumentPart.Document.Body.Elements<SectionProperties>());
            var pageSize = Assert.IsType<PageSize>(section.GetFirstChild<PageSize>());
            Assert.Equal(12240U, pageSize.Width!.Value);
            Assert.Equal(15840U, pageSize.Height!.Value);

            foreach (var table in document.MainDocumentPart.Document.Body.Descendants<Table>())
            {
                var widths = table.GetFirstChild<TableGrid>()!.Elements<GridColumn>()
                    .Select(column => int.Parse(column.Width!.Value!));
                Assert.Equal(9360, widths.Sum());
            }

            var validationErrors = new OpenXmlValidator().Validate(document).ToList();
            Assert.True(validationErrors.Count == 0,
                string.Join(Environment.NewLine, validationErrors.Select(error =>
                    $"{error.Description} Path: {error.Path?.XPath}")));
        }
        finally { Delete(path); }
    }

    [Fact]
    public async Task Word_Export_Embeds_All_Supplied_Screenshots()
    {
        var outputPath = TempPath(".docx");
        var firstImage = TempPath(".png");
        var secondImage = TempPath(".png");
        try
        {
            await File.WriteAllBytesAsync(firstImage, PngBytes());
            await File.WriteAllBytesAsync(secondImage, PngBytes());
            var manual = Manual();
            manual.ScreenshotPaths = [firstImage, secondImage];
            manual.ScreenshotFileNames = ["overview.png", "details.png"];

            await new WordExporter().ExportAsync(manual, outputPath);

            using var document = WordprocessingDocument.Open(outputPath, false);
            Assert.Equal(2, document.MainDocumentPart!.ImageParts.Count());
            Assert.Contains("overview.png", document.MainDocumentPart.Document.Body!.InnerText);
            Assert.Contains("details.png", document.MainDocumentPart.Document.Body.InnerText);
        }
        finally
        {
            Delete(outputPath);
            Delete(firstImage);
            Delete(secondImage);
        }
    }

    [Fact]
    public async Task Pdf_Export_Accepts_Multiple_Screenshots()
    {
        var outputPath = TempPath(".pdf");
        var firstImage = TempPath(".png");
        var secondImage = TempPath(".png");
        try
        {
            await File.WriteAllBytesAsync(firstImage, PngBytes());
            await File.WriteAllBytesAsync(secondImage, PngBytes());
            var manual = Manual();
            manual.ScreenshotPaths = [firstImage, secondImage];
            manual.ScreenshotFileNames = ["overview.png", "details.png"];

            await new PdfExporter().ExportAsync(manual, outputPath);

            using var document = PdfReader.Open(outputPath, PdfDocumentOpenMode.Import);
            Assert.True(document.PageCount >= 2);
        }
        finally
        {
            Delete(outputPath);
            Delete(firstImage);
            Delete(secondImage);
        }
    }

    [Fact]
    public async Task Pdf_Export_Creates_Paginated_Valid_Pdf()
    {
        var path = TempPath(".pdf");
        try
        {
            await new PdfExporter().ExportAsync(Manual(), path);
            using var document = PdfReader.Open(path, PdfDocumentOpenMode.Import);
            Assert.True(document.PageCount >= 2);
            Assert.Equal("Customer Management User Manual", document.Info.Title);
        }
        finally { Delete(path); }
    }

    [Theory]
    [InlineData("screen.txt", UploadKind.Html)]
    [InlineData("manual.pdf", UploadKind.ExistingManual)]
    [InlineData("image.gif", UploadKind.Screenshot)]
    public void Rejects_Unsupported_Extensions(string fileName, UploadKind kind)
    {
        var upload = new UploadedContent { FileName = fileName, Content = [1, 2, 3, 4] };
        Assert.Throws<ValidationException>(() => UploadValidator.Validate(upload, kind, new UploadOptions()));
    }

    [Fact]
    public void Rejects_Path_Traversal()
    {
        var upload = new UploadedContent
        {
            FileName = "../screen.html",
            Content = "<h1>Safe text</h1>"u8.ToArray()
        };
        Assert.Throws<ValidationException>(() =>
            UploadValidator.Validate(upload, UploadKind.Html, new UploadOptions()));
    }

    [Fact]
    public void Rejects_Too_Many_Screenshots()
    {
        var screenshots = Enumerable.Range(1, 3)
            .Select(index => new UploadedContent { FileName = $"screen-{index}.png", Content = PngBytes() })
            .ToList();

        var exception = Assert.Throws<ValidationException>(() =>
            UploadValidator.ValidateScreenshots(screenshots, new UploadOptions { MaxScreenshotCount = 2 }));

        Assert.Contains("maximum of 2", exception.Message);
    }

    [Fact]
    public void Rejects_Screenshots_Over_Combined_Size_Limit()
    {
        var screenshots = new List<UploadedContent>
        {
            new() { FileName = "one.png", Content = PngBytes() },
            new() { FileName = "two.png", Content = PngBytes() }
        };

        var exception = Assert.Throws<ValidationException>(() =>
            UploadValidator.ValidateScreenshots(screenshots, new UploadOptions
            {
                MaxScreenshotCount = 10,
                MaxScreenshotTotalBytes = screenshots.Sum(item => item.Content.Length) - 1
            }));

        Assert.Contains("combined screenshot size", exception.Message);
    }

    private static UserManual Manual() => new()
    {
        Title = "Customer Management User Manual",
        Overview = "Manage customer records.",
        ScreenOverview = "The screen contains search controls and a customer table.",
        GeneratedDate = new DateTimeOffset(2026, 8, 4, 0, 0, 0, TimeSpan.Zero),
        Buttons =
        [
            new ButtonDocumentation
            {
                Name = "Search", Purpose = "Find matching customers.",
                HowToUse = ["Enter a supported search value.", "Select Search."],
                ExpectedResult = "Matching customers are shown."
            }
        ],
        Fields = [new FieldDocumentation { Name = "Search", Purpose = "Search term.", Type = "text" }],
        Tables =
        [
            new TableDocumentation
            {
                Name = "Customers", Purpose = "Lists customer records.",
                Columns = [new ColumnDocumentation { Name = "Customer ID", Description = "Customer identifier." }]
            }
        ]
    };

    private static string TempPath(string extension) => Path.Combine(Path.GetTempPath(), $"manual-{Guid.NewGuid():N}{extension}");
    private static byte[] PngBytes() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
    private static void Delete(string path) { if (File.Exists(path)) File.Delete(path); }
}
