using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using DocumentationGenerator.Application.Contracts;
using DocumentationGenerator.Domain.Models;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;
using WTable = DocumentFormat.OpenXml.Wordprocessing.Table;

namespace DocumentationGenerator.Infrastructure.Export;

public sealed class WordExporter : IWordExporter
{
    public Task ExportAsync(UserManual manual, string outputPath,
        CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var document = WordprocessingDocument.Create(outputPath, WordprocessingDocumentType.Document);
        var mainPart = document.AddMainDocumentPart();
        mainPart.Document = new Document(new Body());
        AddStyles(mainPart);
        var numbering = AddNumbering(mainPart);
        AddFooter(mainPart);
        var body = mainPart.Document.Body!;

        body.Append(Paragraph(manual.Title, "Title", JustificationValues.Center));
        body.Append(Paragraph($"Generated {manual.GeneratedDate:dd MMMM yyyy}", "Subtitle", JustificationValues.Center));
        var screenshots = manual.ScreenshotPaths.Count > 0
            ? manual.ScreenshotPaths
            : string.IsNullOrWhiteSpace(manual.CoverImagePath) ? [] : [manual.CoverImagePath];
        for (var index = 0; index < screenshots.Count; index++)
        {
            if (!File.Exists(screenshots[index])) continue;
            AddImage(mainPart, body, screenshots[index], (uint)(index + 1));
            var caption = index < manual.ScreenshotCaptions.Count &&
                          !string.IsNullOrWhiteSpace(manual.ScreenshotCaptions[index])
                ? manual.ScreenshotCaptions[index]
                : index < manual.ScreenshotFileNames.Count
                ? manual.ScreenshotFileNames[index]
                : $"Screenshot {index + 1}";
            body.Append(Paragraph(caption, "Subtitle", JustificationValues.Center));
        }
        AddText(body, "Overview", manual.Overview);
        AddText(body, "Navigation", manual.Navigation);
        AddText(body, "Screen overview", manual.ScreenOverview);
        AddList(body, "Prerequisites", manual.Prerequisites, false, numbering);
        body.Append(PageBreak());

        if (manual.Buttons.Count > 0)
        {
            body.Append(Paragraph("Feature index", "Heading1"));
            var rows = new List<IReadOnlyList<string>> { new[] { "No.", "Feature", "Description" } };
            rows.AddRange(manual.Buttons.Select((button, index) =>
                (IReadOnlyList<string>)new[] { (index + 1).ToString(), button.Name, button.Purpose }));
            body.Append(CreateTable(rows, [900, 2200, 6260]));
            body.Append(PageBreak());
            for (var index = 0; index < manual.Buttons.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AddButton(body, manual.Buttons[index], index + 1, numbering);
            }
        }

        AddFields(body, manual.Fields);
        foreach (var table in manual.Tables) AddTableDocumentation(body, table, numbering);
        AddSections(body, "Filters", manual.Filters, numbering);
        AddSections(body, "Tabs", manual.Tabs, numbering);
        AddSections(body, "Common procedures", manual.Procedures, numbering);
        AddList(body, "Best practices", manual.BestPractices, false, numbering);
        AddTroubleshooting(body, manual.Troubleshooting);
        AddList(body, "Notes", manual.Notes, false, numbering);
        AddList(body, "Documentation change summary", manual.ChangeSummary, false, numbering);
        AddSources(body, manual.SourceInformation);

        body.Append(new SectionProperties(
            new FooterReference { Type = HeaderFooterValues.Default, Id = mainPart.GetIdOfPart(mainPart.FooterParts.Single()) },
            new PageSize { Width = 12240, Height = 15840, Orient = PageOrientationValues.Portrait },
            new PageMargin { Top = 1440, Right = 1440, Bottom = 1440, Left = 1440, Header = 708, Footer = 708 }));
        numbering.Save();
        mainPart.Document.Save();
    }, cancellationToken);

    private static void AddButton(Body body, ButtonDocumentation button, int number, NumberingContext numbering)
    {
        body.Append(Paragraph($"{number}. {button.Name}", "Heading1"));
        if (!string.IsNullOrWhiteSpace(button.Purpose)) body.Append(Paragraph(button.Purpose));
        AddList(body, "How to use", button.HowToUse, true, numbering, "Heading2");
        AddText(body, "Expected result", button.ExpectedResult, "Heading2");
        AddList(body, "Conditions", button.Conditions, false, numbering, "Heading2");
        AddList(body, "Notes and limitations", button.Notes, false, numbering, "Heading2");
    }

    private static void AddFields(Body body, IReadOnlyCollection<FieldDocumentation> fields)
    {
        if (fields.Count == 0) return;
        body.Append(Paragraph("Fields", "Heading1"));
        var rows = new List<IReadOnlyList<string>> { new[] { "Field", "Purpose", "Type", "Required", "Validation" } };
        rows.AddRange(fields.Select(field => (IReadOnlyList<string>)new[]
        {
            field.Name, field.Purpose, field.Type, field.Required ? "Yes" : "No", string.Join("; ", field.Validation)
        }));
        body.Append(CreateTable(rows, [1700, 3400, 1200, 900, 2160]));
    }

    private static void AddTableDocumentation(Body body, TableDocumentation table, NumberingContext numbering)
    {
        body.Append(Paragraph(table.Name, "Heading1"));
        if (!string.IsNullOrWhiteSpace(table.Purpose)) body.Append(Paragraph(table.Purpose));
        if (table.Columns.Count > 0)
        {
            var rows = new List<IReadOnlyList<string>> { new[] { "Column", "Description" } };
            rows.AddRange(table.Columns.Select(column => (IReadOnlyList<string>)new[] { column.Name, column.Description }));
            body.Append(CreateTable(rows, [2700, 6660]));
        }
        AddList(body, "Row actions", table.RowActions, false, numbering, "Heading2");
        AddText(body, "Sorting", table.Sorting, "Heading2");
        AddText(body, "Filtering", table.Filtering, "Heading2");
        AddText(body, "Pagination", table.Pagination, "Heading2");
    }

    private static void AddSections(Body body, string title, IReadOnlyCollection<ManualSection> sections,
        NumberingContext numbering)
    {
        if (sections.Count == 0) return;
        body.Append(Paragraph(title, "Heading1"));
        foreach (var section in sections)
        {
            body.Append(Paragraph(section.Heading, "Heading2"));
            if (!string.IsNullOrWhiteSpace(section.Summary)) body.Append(Paragraph(section.Summary));
            AddList(body, "Steps", section.Steps, true, numbering, "Heading3");
            AddList(body, "Notes", section.Notes, false, numbering, "Heading3");
        }
    }

    private static void AddTroubleshooting(Body body, IReadOnlyCollection<TroubleshootingItem> items)
    {
        if (items.Count == 0) return;
        body.Append(Paragraph("Troubleshooting", "Heading1"));
        var rows = new List<IReadOnlyList<string>> { new[] { "Problem", "Possible cause", "Solution" } };
        rows.AddRange(items.Select(item => (IReadOnlyList<string>)new[] { item.Problem, item.PossibleCause, item.Solution }));
        body.Append(CreateTable(rows, [3000, 3000, 3360]));
    }

    private static void AddSources(Body body, IReadOnlyCollection<DocumentationGenerator.Domain.Models.SourceReference> sources)
    {
        if (sources.Count == 0) return;
        body.Append(Paragraph("Source information", "Heading1"));
        foreach (var source in sources)
            body.Append(ListParagraph($"{source.SourceType}: {source.FileName} - {source.Summary}", 1));
    }

    private static void AddText(Body body, string heading, string value, string headingStyle = "Heading1")
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        body.Append(Paragraph(heading, headingStyle));
        body.Append(Paragraph(value));
    }

    private static void AddList(Body body, string heading, IReadOnlyCollection<string> items, bool numbered,
        NumberingContext numbering, string headingStyle = "Heading1")
    {
        var values = items.Where(item => !string.IsNullOrWhiteSpace(item)).ToList();
        if (values.Count == 0) return;
        body.Append(Paragraph(heading, headingStyle));
        var numberingId = numbered ? numbering.CreateNumberedList() : 1;
        foreach (var item in values) body.Append(ListParagraph(item, numberingId));
    }

    private static Paragraph Paragraph(string text, string style = "Normal", JustificationValues? justification = null)
    {
        var properties = new ParagraphProperties(new ParagraphStyleId { Val = style });
        if (justification is not null) properties.Append(new Justification { Val = justification });
        return new Paragraph(properties, new Run(new Text(text ?? string.Empty) { Space = SpaceProcessingModeValues.Preserve }));
    }

    private static Paragraph ListParagraph(string text, int numberingId) => new(
        new ParagraphProperties(
            new ParagraphStyleId { Val = "Normal" },
            new NumberingProperties(new NumberingLevelReference { Val = 0 }, new NumberingId { Val = numberingId })),
        new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve }));

    private static Paragraph PageBreak() => new(new Run(new Break { Type = BreakValues.Page }));

    private static WTable CreateTable(IReadOnlyCollection<IReadOnlyList<string>> rows, int[] widths)
    {
        if (widths.Sum() != 9360) throw new InvalidOperationException("Word table widths must total 9360 DXA.");
        var table = new WTable();
        table.Append(new TableProperties(
            new TableWidth { Width = "9360", Type = TableWidthUnitValues.Dxa },
            new TableIndentation { Width = 120, Type = TableWidthUnitValues.Dxa },
            new TableBorders(
                new TopBorder { Val = BorderValues.Single, Size = 4, Color = "9AA4B2" },
                new LeftBorder { Val = BorderValues.Single, Size = 4, Color = "9AA4B2" },
                new BottomBorder { Val = BorderValues.Single, Size = 4, Color = "9AA4B2" },
                new RightBorder { Val = BorderValues.Single, Size = 4, Color = "9AA4B2" },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4, Color = "D6DBE1" },
                new InsideVerticalBorder { Val = BorderValues.Single, Size = 4, Color = "D6DBE1" }),
            new TableLayout { Type = TableLayoutValues.Fixed },
            new TableCellMarginDefault(
                new TopMargin { Width = "80", Type = TableWidthUnitValues.Dxa },
                new TableCellLeftMargin { Width = 120, Type = TableWidthValues.Dxa },
                new BottomMargin { Width = "80", Type = TableWidthUnitValues.Dxa },
                new TableCellRightMargin { Width = 120, Type = TableWidthValues.Dxa })));
        table.Append(new TableGrid(widths.Select(width => new GridColumn { Width = width.ToString() })));
        var rowIndex = 0;
        foreach (var row in rows)
        {
            var rowProperties = new TableRowProperties(new CantSplit());
            if (rowIndex == 0) rowProperties.Append(new TableHeader());
            var tableRow = new TableRow(rowProperties);
            for (var index = 0; index < row.Count; index++)
            {
                var runProperties = rowIndex == 0 ? new RunProperties(new Bold(), new Color { Val = "FFFFFF" }) : null;
                var cellProperties = new TableCellProperties(new TableCellWidth
                {
                    Type = TableWidthUnitValues.Dxa,
                    Width = widths[Math.Min(index, widths.Length - 1)].ToString()
                });
                if (rowIndex == 0) cellProperties.Append(new Shading { Fill = "1F4E78", Val = ShadingPatternValues.Clear });
                cellProperties.Append(new TableCellVerticalAlignment { Val = TableVerticalAlignmentValues.Center });
                var run = new Run(new Text(row[index] ?? string.Empty) { Space = SpaceProcessingModeValues.Preserve });
                if (runProperties is not null) run.PrependChild(runProperties);
                var paragraph = new Paragraph(new ParagraphProperties(new SpacingBetweenLines { After = "60" }), run);
                tableRow.Append(new TableCell(cellProperties, paragraph));
            }
            table.Append(tableRow);
            rowIndex++;
        }
        return table;
    }

    private static void AddStyles(MainDocumentPart mainPart)
    {
        var part = mainPart.AddNewPart<StyleDefinitionsPart>();
        part.Styles = new Styles(
            MakeStyle("Normal", "Normal", 22, "2F3640", false, 0, 120, 300),
            MakeStyle("Title", "Title", 52, "17365D", true, 0, 120, 300),
            MakeStyle("Subtitle", "Subtitle", 20, "5F6B78", false, 0, 120, 300),
            MakeStyle("Heading1", "Heading 1", 32, "2E74B5", true, 360, 200, 300),
            MakeStyle("Heading2", "Heading 2", 26, "2E74B5", true, 280, 140, 300),
            MakeStyle("Heading3", "Heading 3", 24, "1F4D78", true, 200, 100, 300));
        part.Styles.Save();
    }

    private static Style MakeStyle(string id, string name, int halfPoints, string color, bool bold, int before, int after, int line)
    {
        var runProperties = new StyleRunProperties(
            new RunFonts { Ascii = "Calibri", HighAnsi = "Calibri" });
        if (bold) runProperties.Append(new Bold());
        runProperties.Append(
            new Color { Val = color },
            new FontSize { Val = halfPoints.ToString() });
        return new Style(
            new StyleName { Val = name },
            new BasedOn { Val = "Normal" },
            new NextParagraphStyle { Val = "Normal" },
            new StyleParagraphProperties(new SpacingBetweenLines { Before = before.ToString(), After = after.ToString(), Line = line.ToString(), LineRule = LineSpacingRuleValues.Auto }),
            runProperties)
        { Type = StyleValues.Paragraph, StyleId = id, Default = id == "Normal" };
    }

    private static NumberingContext AddNumbering(MainDocumentPart mainPart)
    {
        var part = mainPart.AddNewPart<NumberingDefinitionsPart>();
        var bullet = new AbstractNum(
            new Level(
                new NumberingFormat { Val = NumberFormatValues.Bullet },
                new LevelText { Val = "-" },
                new LevelJustification { Val = LevelJustificationValues.Left },
                new PreviousParagraphProperties(
                    new Tabs(new TabStop { Val = TabStopValues.Number, Position = 540 }),
                    new SpacingBetweenLines { After = "80", Line = "300", LineRule = LineSpacingRuleValues.Auto },
                    new Indentation { Left = "540", Hanging = "270" })) { LevelIndex = 0 })
        { AbstractNumberId = 1 };
        var decimalNumber = new AbstractNum(
            new Level(
                new NumberingFormat { Val = NumberFormatValues.Decimal },
                new LevelText { Val = "%1." },
                new LevelJustification { Val = LevelJustificationValues.Left },
                new PreviousParagraphProperties(
                    new Tabs(new TabStop { Val = TabStopValues.Number, Position = 540 }),
                    new SpacingBetweenLines { After = "80", Line = "300", LineRule = LineSpacingRuleValues.Auto },
                    new Indentation { Left = "540", Hanging = "270" }))
            { LevelIndex = 0, StartNumberingValue = new StartNumberingValue { Val = 1 } })
        { AbstractNumberId = 2 };
        part.Numbering = new Numbering(bullet, decimalNumber,
            new NumberingInstance(new AbstractNumId { Val = 1 }) { NumberID = 1 });
        part.Numbering.Save();
        return new NumberingContext(part);
    }

    private sealed class NumberingContext(NumberingDefinitionsPart part)
    {
        private int _nextNumberingId = 2;

        public int CreateNumberedList()
        {
            var numberingId = _nextNumberingId++;
            part.Numbering!.Append(new NumberingInstance(
                new AbstractNumId { Val = 2 },
                new LevelOverride(new StartOverrideNumberingValue { Val = 1 }) { LevelIndex = 0 })
            { NumberID = numberingId });
            return numberingId;
        }

        public void Save() => part.Numbering!.Save();
    }

    private static void AddFooter(MainDocumentPart mainPart)
    {
        var footerPart = mainPart.AddNewPart<FooterPart>();
        footerPart.Footer = new Footer(new Paragraph(
            new ParagraphProperties(new Justification { Val = JustificationValues.Center }),
            new Run(new Text("AI-Powered User Manual Generator  |  ")),
            new Run(new FieldChar { FieldCharType = FieldCharValues.Begin }),
            new Run(new FieldCode(" PAGE ")),
            new Run(new FieldChar { FieldCharType = FieldCharValues.End })));
        footerPart.Footer.Save();
    }

    private static void AddImage(MainDocumentPart mainPart, Body body, string path, uint imageId)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is not (".png" or ".jpg" or ".jpeg")) return;
        var imagePart = mainPart.AddImagePart(extension == ".png" ? "image/png" : "image/jpeg");
        using (var stream = File.OpenRead(path)) imagePart.FeedData(stream);
        const long width = 5_850_000;
        const long height = 3_290_625;
        var relationshipId = mainPart.GetIdOfPart(imagePart);
        var drawing = new Drawing(
            new DW.Inline(
                new DW.Extent { Cx = width, Cy = height },
                new DW.EffectExtent { LeftEdge = 0L, TopEdge = 0L, RightEdge = 0L, BottomEdge = 0L },
                new DW.DocProperties { Id = imageId, Name = $"Application screenshot {imageId}" },
                new DW.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks { NoChangeAspect = true }),
                new A.Graphic(new A.GraphicData(
                    new PIC.Picture(
                        new PIC.NonVisualPictureProperties(
                            new PIC.NonVisualDrawingProperties { Id = 0U, Name = Path.GetFileName(path) },
                            new PIC.NonVisualPictureDrawingProperties()),
                        new PIC.BlipFill(new A.Blip { Embed = relationshipId }, new A.Stretch(new A.FillRectangle())),
                        new PIC.ShapeProperties(
                            new A.Transform2D(new A.Offset { X = 0L, Y = 0L }, new A.Extents { Cx = width, Cy = height }),
                            new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle })))
                { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" }))
            { DistanceFromTop = 0U, DistanceFromBottom = 0U, DistanceFromLeft = 0U, DistanceFromRight = 0U });
        body.Append(new Paragraph(new ParagraphProperties(new Justification { Val = JustificationValues.Center }), new Run(drawing)));
    }
}
