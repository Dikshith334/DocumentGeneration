using System.Text.Json.Serialization;

namespace DocumentationGenerator.Domain.Models;

public sealed class Screen
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string SourceFileName { get; set; } = string.Empty;
    public string? ScreenshotPath { get; set; }
    public List<string> ScreenshotPaths { get; set; } = [];
    public List<string> ScreenshotFileNames { get; set; } = [];
    public string? ExistingManualPath { get; set; }
    public string Description { get; set; } = string.Empty;
    public string NavigationPath { get; set; } = string.Empty;
    public List<Button> Buttons { get; set; } = [];
    public List<InputField> InputFields { get; set; } = [];
    public List<Dropdown> Dropdowns { get; set; } = [];
    public List<TableDefinition> Tables { get; set; } = [];
    public List<FilterDefinition> Filters { get; set; } = [];
    public List<TabDefinition> Tabs { get; set; } = [];
    public List<LinkDefinition> Links { get; set; } = [];
    public List<string> Labels { get; set; } = [];
    public List<string> DetectedSections { get; set; } = [];
    public List<string> ScreenshotObservations { get; set; } = [];
    public string BusinessRules { get; set; } = string.Empty;

    public int ElementCount => Buttons.Count + InputFields.Count + Dropdowns.Count +
        Tables.Count + Filters.Count + Tabs.Count + Links.Count;
}

public sealed class Button
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Text { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ElementId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string Tooltip { get; set; } = string.Empty;
    public string CssClasses { get; set; } = string.Empty;
    public string AngularClickHandler { get; set; } = string.Empty;
    public string VisibilityCondition { get; set; } = string.Empty;
    public string DisabledCondition { get; set; } = string.Empty;
    public string AriaLabel { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
    public string SourceSnippet { get; set; } = string.Empty;

    public string DisplayName => FirstNonEmpty(Text, AriaLabel, Tooltip, Name, ElementId, Icon, "Unnamed button");

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
}

public sealed class InputField
{
    public string Label { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ElementId { get; set; } = string.Empty;
    public string InputType { get; set; } = "text";
    public string Placeholder { get; set; } = string.Empty;
    public bool Required { get; set; }
    public bool ReadOnly { get; set; }
    public bool Disabled { get; set; }
    public Dictionary<string, string> ValidationAttributes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string AngularModelBinding { get; set; } = string.Empty;
    public string VisibilityCondition { get; set; } = string.Empty;

    public string DisplayName => FirstNonEmpty(Label, Placeholder, Name, ElementId, "Unnamed field");

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
}

public sealed class Dropdown
{
    public string Label { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public List<string> Options { get; set; } = [];
    public string Placeholder { get; set; } = string.Empty;
    public bool Required { get; set; }
    public string AngularModelBinding { get; set; } = string.Empty;
    public string ChangeHandler { get; set; } = string.Empty;
    public string VisibilityCondition { get; set; } = string.Empty;

    public string DisplayName => new[] { Label, Placeholder, Name }
        .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "Unnamed dropdown";
}

public sealed class TableDefinition
{
    public string Name { get; set; } = string.Empty;
    public List<string> Headers { get; set; } = [];
    public List<string> ActionColumns { get; set; } = [];
    public List<string> SortableColumns { get; set; } = [];
    public List<string> FilterableColumns { get; set; } = [];
    public string PaginationInformation { get; set; } = string.Empty;
    public string EmptyStateText { get; set; } = string.Empty;
}

public sealed class FilterDefinition
{
    public string Label { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Placeholder { get; set; } = string.Empty;
    public string TargetField { get; set; } = string.Empty;
    public string DefaultValue { get; set; } = string.Empty;
    public List<string> AvailableOptions { get; set; } = [];
}

public sealed class TabDefinition
{
    public string Title { get; set; } = string.Empty;
    public string Identifier { get; set; } = string.Empty;
    public string VisibilityCondition { get; set; } = string.Empty;
    public string ActiveCondition { get; set; } = string.Empty;
}

public sealed class LinkDefinition
{
    public string Text { get; set; } = string.Empty;
    public string Href { get; set; } = string.Empty;
    public string AngularRoute { get; set; } = string.Empty;
}

public sealed class ExistingManual
{
    public string FileName { get; set; } = string.Empty;
    public string PlainText { get; set; } = string.Empty;
    public List<ManualParagraph> Paragraphs { get; set; } = [];
    public List<string> Headings { get; set; } = [];
    public List<ManualTable> Tables { get; set; } = [];
    public List<string> DetectedStyleObservations { get; set; } = [];
}

public sealed class ManualParagraph
{
    public int Order { get; set; }
    public string Text { get; set; } = string.Empty;
    public string StyleName { get; set; } = string.Empty;
    public bool IsHeading { get; set; }
}

public sealed class ManualTable
{
    public int Order { get; set; }
    public List<List<string>> Rows { get; set; } = [];
}

public sealed class DocumentationChangeSet
{
    public List<DocumentationElement> AddedElements { get; set; } = [];
    public List<DocumentationElement> RemovedElements { get; set; } = [];
    public List<DocumentationElement> ExistingElements { get; set; } = [];
    public List<DocumentationElement> PossiblyChangedElements { get; set; } = [];
    public List<DocumentationElement> UndocumentedElements { get; set; } = [];
    public List<DocumentationElement> PossiblyRemovedElements { get; set; } = [];
    public string Summary { get; set; } = string.Empty;
}

public sealed class DocumentationElement
{
    public string Category { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Evidence { get; set; } = string.Empty;
    public string? PossibleMatch { get; set; }
    public bool RequiresReview { get; set; }
}

public sealed class ScreenshotAnalysisResult
{
    public string SourceFileName { get; set; } = string.Empty;
    public bool Succeeded { get; set; }
    public string ScreenTitle { get; set; } = string.Empty;
    public List<string> MainSections { get; set; } = [];
    public List<string> Buttons { get; set; } = [];
    public List<string> Forms { get; set; } = [];
    public List<string> Tables { get; set; } = [];
    public List<string> Charts { get; set; } = [];
    public List<string> Tabs { get; set; } = [];
    public List<string> Filters { get; set; } = [];
    public List<string> Icons { get; set; } = [];
    public List<string> NavigationElements { get; set; } = [];
    public List<string> LayoutObservations { get; set; } = [];
    public List<string> Messages { get; set; } = [];
    public string? Warning { get; set; }

    public IEnumerable<string> AllObservations() => MainSections.Select(x => $"Section: {x}")
        .Concat(Buttons.Select(x => $"Visible button: {x}"))
        .Concat(Tables.Select(x => $"Visible table: {x}"))
        .Concat(LayoutObservations);
}

public sealed class UserManual
{
    public string Title { get; set; } = string.Empty;
    public string Overview { get; set; } = string.Empty;
    public string Navigation { get; set; } = string.Empty;
    public string ScreenOverview { get; set; } = string.Empty;
    public List<string> Prerequisites { get; set; } = [];
    public List<ButtonDocumentation> Buttons { get; set; } = [];
    public List<FieldDocumentation> Fields { get; set; } = [];
    public List<TableDocumentation> Tables { get; set; } = [];
    public List<ManualSection> Filters { get; set; } = [];
    public List<ManualSection> Tabs { get; set; } = [];
    public List<ManualSection> Procedures { get; set; } = [];
    public List<string> BestPractices { get; set; } = [];
    public List<TroubleshootingItem> Troubleshooting { get; set; } = [];
    public List<string> Notes { get; set; } = [];
    public List<string> ChangeSummary { get; set; } = [];
    public DateTimeOffset GeneratedDate { get; set; } = DateTimeOffset.UtcNow;
    public List<SourceReference> SourceInformation { get; set; } = [];

    [JsonIgnore]
    public string? CoverImagePath { get; set; }

    [JsonIgnore]
    public List<string> ScreenshotPaths { get; set; } = [];

    [JsonIgnore]
    public List<string> ScreenshotFileNames { get; set; } = [];
}

public sealed class ManualSection
{
    public string Heading { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public List<string> Steps { get; set; } = [];
    public List<string> Notes { get; set; } = [];
}

public sealed class ButtonDocumentation
{
    public string Name { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
    public List<string> HowToUse { get; set; } = [];
    public string ExpectedResult { get; set; } = string.Empty;
    public List<string> Conditions { get; set; } = [];
    public List<string> Notes { get; set; } = [];
}

public sealed class FieldDocumentation
{
    public string Name { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public bool Required { get; set; }
    public List<string> Validation { get; set; } = [];
    public string Example { get; set; } = string.Empty;
    public List<string> Notes { get; set; } = [];
}

public sealed class TableDocumentation
{
    public string Name { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
    public List<ColumnDocumentation> Columns { get; set; } = [];
    public List<string> RowActions { get; set; } = [];
    public string Sorting { get; set; } = string.Empty;
    public string Filtering { get; set; } = string.Empty;
    public string Pagination { get; set; } = string.Empty;
}

public sealed class ColumnDocumentation
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public sealed class TroubleshootingItem
{
    public string Problem { get; set; } = string.Empty;
    public string PossibleCause { get; set; } = string.Empty;
    public string Solution { get; set; } = string.Empty;
}

public sealed class SourceReference
{
    public string SourceType { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
}

public sealed class UploadedContent
{
    public required string FileName { get; init; }
    public required byte[] Content { get; init; }
}

public sealed class AnalysisRequest
{
    public string ScreenName { get; set; } = string.Empty;
    public required UploadedContent HtmlFile { get; init; }
    public UploadedContent? ExistingManual { get; init; }
    public UploadedContent? Screenshot { get; init; }
    public List<UploadedContent> Screenshots { get; init; } = [];
    public string BusinessRules { get; set; } = string.Empty;
    public string VisionModel { get; set; } = string.Empty;
}

public sealed class AnalysisResult
{
    public Guid JobId { get; set; }
    public Screen Screen { get; set; } = new();
    public ExistingManual? ExistingManual { get; set; }
    public ScreenshotAnalysisResult? ScreenshotAnalysis { get; set; }
    public List<ScreenshotAnalysisResult> ScreenshotAnalyses { get; set; } = [];
    public DocumentationChangeSet Changes { get; set; } = new();
    public List<string> Warnings { get; set; } = [];
}

public sealed class GenerationRequest
{
    public Guid JobId { get; set; }
    public string TextModel { get; set; } = string.Empty;
}

public sealed class GenerationResult
{
    public Guid JobId { get; set; }
    public bool Succeeded { get; set; }
    public UserManual? Manual { get; set; }
    public string MarkdownFileName { get; set; } = string.Empty;
    public string WordFileName { get; set; } = string.Empty;
    public string PdfFileName { get; set; } = string.Empty;
    public List<string> Warnings { get; set; } = [];
    public string? ErrorMessage { get; set; }
}

public sealed record OllamaModel(string Name, long Size, DateTimeOffset? ModifiedAt);

public sealed record OllamaHealthResult(bool IsAvailable, string Message);

public sealed class JobSnapshot
{
    public AnalysisResult Analysis { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ValidationException(string message) : Exception(message);

public sealed class OllamaUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public sealed class OllamaResponseException(string message) : Exception(message);
