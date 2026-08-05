using DocumentationGenerator.Application.Contracts;
using DocumentationGenerator.Domain.Models;
using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;

namespace DocumentationGenerator.Infrastructure.Export;

public sealed class PdfExporter : IPdfExporter
{
    public Task ExportAsync(UserManual manual, string outputPath,
        CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureFontResolver();
        using var document = new PdfDocument();
        document.Info.Title = manual.Title;
        document.Info.Author = "AI-Powered User Manual Generator";
        var writer = new PdfLayout(document, cancellationToken);

        writer.Title(manual.Title);
        writer.Centered($"Generated {manual.GeneratedDate:dd MMMM yyyy}", muted: true);
        writer.Space(12);
        var screenshots = manual.ScreenshotPaths.Count > 0
            ? manual.ScreenshotPaths
            : string.IsNullOrWhiteSpace(manual.CoverImagePath) ? [] : [manual.CoverImagePath];
        for (var index = 0; index < screenshots.Count; index++)
        {
            if (!File.Exists(screenshots[index])) continue;
            writer.Image(screenshots[index]);
            writer.Centered(index < manual.ScreenshotFileNames.Count
                ? manual.ScreenshotFileNames[index]
                : $"Screenshot {index + 1}", muted: true);
            writer.Space(8);
        }
        writer.TextSection("Overview", manual.Overview);
        writer.TextSection("Navigation", manual.Navigation);
        writer.TextSection("Screen overview", manual.ScreenOverview);
        writer.ListSection("Prerequisites", manual.Prerequisites);
        writer.PageBreak();

        if (manual.Buttons.Count > 0)
        {
            writer.Heading("Feature index", 1);
            var rows = new List<string[]> { new[] { "No.", "Feature", "Description" } };
            rows.AddRange(manual.Buttons.Select((button, index) => new[]
                { (index + 1).ToString(), button.Name, button.Purpose }));
            writer.Table(rows, [34, 145, 319]);
            writer.PageBreak();
            for (var index = 0; index < manual.Buttons.Count; index++)
            {
                var button = manual.Buttons[index];
                writer.Heading($"{index + 1}. {button.Name}", 1);
                writer.Paragraph(button.Purpose);
                writer.NumberedSection("How to use", button.HowToUse);
                writer.TextSection("Expected result", button.ExpectedResult, 2);
                writer.ListSection("Conditions", button.Conditions, 2);
                writer.ListSection("Notes and limitations", button.Notes, 2);
            }
        }

        if (manual.Fields.Count > 0)
        {
            writer.Heading("Fields", 1);
            var rows = new List<string[]> { new[] { "Field", "Purpose", "Type", "Required", "Validation" } };
            rows.AddRange(manual.Fields.Select(field => new[]
            {
                field.Name, field.Purpose, field.Type, field.Required ? "Yes" : "No", string.Join("; ", field.Validation)
            }));
            writer.Table(rows, [90, 190, 62, 58, 98]);
        }

        foreach (var table in manual.Tables)
        {
            writer.Heading(table.Name, 1);
            writer.Paragraph(table.Purpose);
            if (table.Columns.Count > 0)
            {
                var rows = new List<string[]> { new[] { "Column", "Description" } };
                rows.AddRange(table.Columns.Select(column => new[] { column.Name, column.Description }));
                writer.Table(rows, [145, 353]);
            }
            writer.ListSection("Row actions", table.RowActions, 2);
            writer.TextSection("Sorting", table.Sorting, 2);
            writer.TextSection("Filtering", table.Filtering, 2);
            writer.TextSection("Pagination", table.Pagination, 2);
        }

        writer.Sections("Filters", manual.Filters);
        writer.Sections("Tabs", manual.Tabs);
        writer.Sections("Common procedures", manual.Procedures);
        writer.ListSection("Best practices", manual.BestPractices);
        if (manual.Troubleshooting.Count > 0)
        {
            writer.Heading("Troubleshooting", 1);
            var rows = new List<string[]> { new[] { "Problem", "Possible cause", "Solution" } };
            rows.AddRange(manual.Troubleshooting.Select(item => new[] { item.Problem, item.PossibleCause, item.Solution }));
            writer.Table(rows, [150, 150, 198]);
        }
        writer.ListSection("Notes", manual.Notes);
        writer.ListSection("Documentation change summary", manual.ChangeSummary);
        if (manual.SourceInformation.Count > 0)
        {
            writer.Heading("Source information", 1);
            writer.Bullets(manual.SourceInformation.Select(source =>
                $"{source.SourceType}: {source.FileName} - {source.Summary}").ToList());
        }

        writer.Dispose();
        document.Save(outputPath);
    }, cancellationToken);

    private sealed class PdfLayout : IDisposable
    {
        private const double Margin = 54;
        private const double Bottom = 54;
        private readonly PdfDocument _document;
        private readonly CancellationToken _cancellationToken;
        private readonly XFont _body = new("ManualSans", 10, XFontStyleEx.Regular);
        private readonly XFont _bodyBold = new("ManualSans", 10, XFontStyleEx.Bold);
        private readonly XFont _title = new("ManualSans", 19, XFontStyleEx.Bold);
        private readonly XFont _heading1 = new("ManualSans", 14, XFontStyleEx.Bold);
        private readonly XFont _heading2 = new("ManualSans", 11.5, XFontStyleEx.Bold);
        private readonly XFont _small = new("ManualSans", 8, XFontStyleEx.Regular);
        private readonly XBrush _text = new XSolidBrush(XColor.FromArgb(47, 54, 64));
        private readonly XBrush _heading = new XSolidBrush(XColor.FromArgb(23, 54, 93));
        private readonly XBrush _muted = new XSolidBrush(XColor.FromArgb(95, 107, 120));
        private PdfPage _page = null!;
        private XGraphics _graphics = null!;
        private double _y;

        public PdfLayout(PdfDocument document, CancellationToken cancellationToken)
        {
            _document = document;
            _cancellationToken = cancellationToken;
            NewPage();
        }

        private double ContentWidth => _page.Width.Point - (Margin * 2);
        private double ContentBottom => _page.Height.Point - Bottom;

        public void Title(string text)
        {
            Ensure(40);
            _graphics.DrawString(text, _title, _heading,
                new XRect(Margin, _y, ContentWidth, 30), XStringFormats.TopCenter);
            _y += 34;
            _graphics.DrawLine(new XPen(XColor.FromArgb(31, 78, 120), 1.2), Margin + 90, _y, _page.Width.Point - Margin - 90, _y);
            _y += 12;
        }

        public void Centered(string text, bool muted = false)
        {
            Ensure(18);
            _graphics.DrawString(text, _body, muted ? _muted : _text,
                new XRect(Margin, _y, ContentWidth, 16), XStringFormats.TopCenter);
            _y += 18;
        }

        public void Image(string path)
        {
            var extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension is not (".png" or ".jpg" or ".jpeg")) return;
            using var image = XImage.FromFile(path);
            var maxWidth = ContentWidth;
            var maxHeight = 250d;
            var ratio = Math.Min(maxWidth / image.PixelWidth, maxHeight / image.PixelHeight);
            var width = image.PixelWidth * ratio;
            var height = image.PixelHeight * ratio;
            Ensure(height + 16);
            _graphics.DrawImage(image, Margin + (ContentWidth - width) / 2, _y, width, height);
            _y += height + 16;
        }

        public void Heading(string text, int level)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            var font = level == 1 ? _heading1 : _heading2;
            var lines = Wrap(text, font, ContentWidth);
            Ensure(lines.Count * LineHeight(font) + 12);
            _y += level == 1 ? 8 : 4;
            foreach (var line in lines)
            {
                DrawLine(line, font, _heading, Margin, ContentWidth);
                _y += LineHeight(font);
            }
            _y += 4;
        }

        public void Paragraph(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            foreach (var sourceParagraph in text.Replace("\r", string.Empty).Split('\n'))
            {
                var lines = Wrap(sourceParagraph, _body, ContentWidth);
                foreach (var line in lines)
                {
                    Ensure(LineHeight(_body));
                    DrawLine(line, _body, _text, Margin, ContentWidth);
                    _y += LineHeight(_body);
                }
                _y += 5;
            }
        }

        public void TextSection(string heading, string value, int level = 1)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            Heading(heading, level);
            Paragraph(value);
        }

        public void ListSection(string heading, IReadOnlyCollection<string> items, int level = 1)
        {
            var values = items.Where(item => !string.IsNullOrWhiteSpace(item)).ToList();
            if (values.Count == 0) return;
            Heading(heading, level);
            Bullets(values);
        }

        public void NumberedSection(string heading, IReadOnlyCollection<string> items)
        {
            var values = items.Where(item => !string.IsNullOrWhiteSpace(item)).ToList();
            if (values.Count == 0) return;
            Heading(heading, 2);
            for (var index = 0; index < values.Count; index++) ListItem($"{index + 1}.", values[index]);
        }

        public void Bullets(IReadOnlyCollection<string> items)
        {
            foreach (var item in items.Where(item => !string.IsNullOrWhiteSpace(item))) ListItem("-", item);
        }

        private void ListItem(string marker, string value)
        {
            var lines = Wrap(value, _body, ContentWidth - 24);
            Ensure(lines.Count * LineHeight(_body) + 3);
            DrawLine(marker, _bodyBold, _heading, Margin + 4, 18);
            foreach (var line in lines)
            {
                DrawLine(line, _body, _text, Margin + 24, ContentWidth - 24);
                _y += LineHeight(_body);
            }
            _y += 3;
        }

        public void Sections(string title, IReadOnlyCollection<ManualSection> sections)
        {
            if (sections.Count == 0) return;
            Heading(title, 1);
            foreach (var section in sections)
            {
                Heading(section.Heading, 2);
                Paragraph(section.Summary);
                NumberedSection("Steps", section.Steps);
                ListSection("Notes", section.Notes, 2);
            }
        }

        public void Table(IReadOnlyList<string[]> rows, double[] widths)
        {
            if (rows.Count == 0) return;
            var normalizedWidths = NormalizeWidths(widths);
            for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var font = rowIndex == 0 ? _bodyBold : _small;
                var wrapped = rows[rowIndex].Select((cell, index) =>
                    Wrap(cell ?? string.Empty, font, normalizedWidths[Math.Min(index, normalizedWidths.Length - 1)] - 8)).ToList();
                var rowHeight = Math.Max(18, wrapped.Max(lines => lines.Count) * LineHeight(font) + 8);
                if (_y + rowHeight > ContentBottom)
                {
                    NewPage();
                    if (rowIndex > 0) DrawTableRow(rows[0], normalizedWidths, true);
                }
                DrawTableRow(rows[rowIndex], normalizedWidths, rowIndex == 0, rowHeight, wrapped);
            }
            _y += 10;
        }

        private void DrawTableRow(string[] cells, double[] widths, bool header, double? fixedHeight = null,
            IReadOnlyList<List<string>>? preWrapped = null)
        {
            var font = header ? _bodyBold : _small;
            var wrapped = preWrapped ?? cells.Select((cell, index) =>
                Wrap(cell ?? string.Empty, font, widths[Math.Min(index, widths.Length - 1)] - 8)).ToList();
            var height = fixedHeight ?? Math.Max(18, wrapped.Max(lines => lines.Count) * LineHeight(font) + 8);
            var x = Margin;
            for (var index = 0; index < cells.Length; index++)
            {
                var width = widths[Math.Min(index, widths.Length - 1)];
                var fill = header ? new XSolidBrush(XColor.FromArgb(31, 78, 120)) :
                    new XSolidBrush(index % 2 == 0 ? XColor.FromArgb(248, 250, 252) : XColors.White);
                _graphics.DrawRectangle(fill, x, _y, width, height);
                _graphics.DrawRectangle(new XPen(XColor.FromArgb(185, 195, 205), 0.5), x, _y, width, height);
                var lineY = _y + 4;
                foreach (var line in wrapped[Math.Min(index, wrapped.Count - 1)])
                {
                    _graphics.DrawString(line, font, header ? XBrushes.White : _text,
                        new XRect(x + 4, lineY, width - 8, LineHeight(font)), XStringFormats.TopLeft);
                    lineY += LineHeight(font);
                }
                x += width;
            }
            _y += height;
        }

        public void PageBreak()
        {
            if (_y <= Margin + 4) return;
            NewPage();
        }

        public void Space(double points)
        {
            Ensure(points);
            _y += points;
        }

        private double[] NormalizeWidths(double[] widths)
        {
            var total = widths.Sum();
            return widths.Select(width => width * ContentWidth / total).ToArray();
        }

        private void Ensure(double height)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (_y + height > ContentBottom) NewPage();
        }

        private void NewPage()
        {
            _graphics?.Dispose();
            _page = _document.AddPage();
            _page.Size = PageSize.Letter;
            _graphics = XGraphics.FromPdfPage(_page);
            _y = Margin;
            var pageNumber = _document.PageCount;
            _graphics.DrawLine(new XPen(XColor.FromArgb(205, 212, 220), 0.5), Margin, _page.Height.Point - 40,
                _page.Width.Point - Margin, _page.Height.Point - 40);
            _graphics.DrawString($"AI-Powered User Manual Generator  |  {pageNumber}", _small, _muted,
                new XRect(Margin, _page.Height.Point - 34, ContentWidth, 12), XStringFormats.TopCenter);
        }

        private List<string> Wrap(string text, XFont font, double width)
        {
            var words = (text ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) return [string.Empty];
            var lines = new List<string>();
            var current = string.Empty;
            foreach (var word in words)
            {
                var candidate = string.IsNullOrEmpty(current) ? word : current + " " + word;
                if (_graphics.MeasureString(candidate, font).Width <= width || string.IsNullOrEmpty(current)) current = candidate;
                else
                {
                    lines.Add(current);
                    current = word;
                }
            }
            if (!string.IsNullOrEmpty(current)) lines.Add(current);
            return lines;
        }

        private void DrawLine(string line, XFont font, XBrush brush, double x, double width) =>
            _graphics.DrawString(line, font, brush, new XRect(x, _y, width, LineHeight(font)), XStringFormats.TopLeft);

        private double LineHeight(XFont font) => Math.Max(font.Size + 3, _graphics.MeasureString("Ag", font).Height + 1);

        public void Dispose() => _graphics.Dispose();
    }

    private static readonly object FontLock = new();

    private static void EnsureFontResolver()
    {
        if (GlobalFontSettings.FontResolver is not null) return;
        lock (FontLock)
        {
            GlobalFontSettings.FontResolver ??= new CrossPlatformFontResolver();
        }
    }

    private sealed class CrossPlatformFontResolver : IFontResolver
    {
        private readonly Dictionary<string, string> _fontPaths;

        public CrossPlatformFontResolver()
        {
            var regularCandidates = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arial.ttf"),
                "/usr/share/fonts/truetype/liberation2/LiberationSans-Regular.ttf",
                "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
                "/Library/Fonts/Arial.ttf",
                "/System/Library/Fonts/Supplemental/Arial.ttf"
            };
            var boldCandidates = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arialbd.ttf"),
                "/usr/share/fonts/truetype/liberation2/LiberationSans-Bold.ttf",
                "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
                "/Library/Fonts/Arial Bold.ttf",
                "/System/Library/Fonts/Supplemental/Arial Bold.ttf"
            };
            var regular = regularCandidates.FirstOrDefault(File.Exists)
                          ?? throw new InvalidOperationException("No supported system sans-serif font was found for PDF export.");
            var bold = boldCandidates.FirstOrDefault(File.Exists) ?? regular;
            _fontPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["manual-regular"] = regular,
                ["manual-bold"] = bold
            };
        }

        public byte[] GetFont(string faceName) => File.ReadAllBytes(_fontPaths[faceName]);

        public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic) =>
            new(isBold ? "manual-bold" : "manual-regular", false, isItalic);
    }
}
