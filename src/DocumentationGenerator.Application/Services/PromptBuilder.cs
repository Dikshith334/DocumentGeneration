using System.Text;
using System.Text.Json;
using DocumentationGenerator.Application.Contracts;
using DocumentationGenerator.Domain.Models;

namespace DocumentationGenerator.Application.Services;

public sealed class PromptBuilder : IPromptBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public string Build(Screen screen, ExistingManual? manual, ScreenshotAnalysisResult? screenshot,
        DocumentationChangeSet changes)
    {
        var prompt = new StringBuilder();
        prompt.AppendLine("You are a professional technical writer creating a task-oriented application user manual.");
        prompt.AppendLine("Return one strict JSON object only. Do not use Markdown fences or add text outside JSON.");
        prompt.AppendLine();
        prompt.AppendLine("SOURCE PRIORITY AND NON-NEGOTIABLE RULES:");
        prompt.AppendLine("1. Explicit business rules have highest priority and must never be contradicted.");
        prompt.AppendLine("2. HTML attributes and bindings override screenshot-based guesses.");
        prompt.AppendLine("3. Use only supplied sources. Do not invent behavior, permissions, navigation paths, validation, examples, or outcomes.");
        prompt.AppendLine("4. Preserve accurate existing content, update outdated content, and add newly detected UI elements.");
        prompt.AppendLine("5. If information is unavailable, write 'Not specified' or clearly flag it for review.");
        prompt.AppendLine("6. A screenshot observation describes visible UI only; it is not proof of behavior.");
        prompt.AppendLine("7. Omit sections that would add no useful information.");
        prompt.AppendLine();
        prompt.AppendLine("EXPLICIT BUSINESS RULES (highest priority):");
        prompt.AppendLine(string.IsNullOrWhiteSpace(screen.BusinessRules) ? "No explicit business rules supplied." : screen.BusinessRules.Trim());
        prompt.AppendLine();
        prompt.AppendLine("EXTRACTED SCREEN MODEL:");
        prompt.AppendLine(JsonSerializer.Serialize(screen, JsonOptions));
        prompt.AppendLine();
        prompt.AppendLine("EXISTING MANUAL AND WRITING STYLE:");
        prompt.AppendLine(manual is null ? "No existing manual supplied." : JsonSerializer.Serialize(manual, JsonOptions));
        prompt.AppendLine();
        prompt.AppendLine("VISIBLE SCREENSHOT OBSERVATIONS:");
        prompt.AppendLine(screenshot is null ? "No screenshot analysis supplied." : JsonSerializer.Serialize(screenshot, JsonOptions));
        prompt.AppendLine();
        prompt.AppendLine("DOCUMENTATION CHANGE ANALYSIS:");
        prompt.AppendLine(JsonSerializer.Serialize(changes, JsonOptions));
        prompt.AppendLine();
        prompt.AppendLine("REQUIRED JSON SHAPE:");
        prompt.AppendLine(JsonSerializer.Serialize(CreateExample(screen), JsonOptions));
        prompt.AppendLine();
        prompt.AppendLine("JSON TYPE REQUIREMENTS:");
        prompt.AppendLine("- Every scalar property shown as text must be a JSON string, never an array or object.");
        prompt.AppendLine("- sorting, filtering, pagination, purpose, summary, expectedResult, example, possibleCause, and solution are strings.");
        prompt.AppendLine("- prerequisites, howToUse, conditions, notes, validation, rowActions, steps, and bestPractices are arrays of strings.");
        prompt.AppendLine("- Use an empty string or empty array when a value is unavailable; do not change the property's JSON type.");
        prompt.AppendLine("- Never copy instructional phrases or example labels into the response. Write actual documentation from the supplied sources.");
        prompt.AppendLine();
        prompt.AppendLine("For each button include name, purpose, howToUse, expectedResult, conditions, and notes.");
        prompt.AppendLine("For fields include type, required status, supplied validation, and examples only when a source supports them.");
        prompt.AppendLine("For tables include columns, row actions, sorting, filtering, and pagination only when supported.");
        return prompt.ToString();
    }

    private static UserManual CreateExample(Screen screen) => new()
    {
        Title = $"{screen.Name} User Manual",
        Overview = ValueOrNotSpecified(screen.Description),
        Navigation = ValueOrNotSpecified(screen.NavigationPath),
        ScreenOverview = ValueOrNotSpecified(screen.Description),
        Prerequisites = [],
        Buttons = screen.Buttons.Select(button => new ButtonDocumentation
        {
            Name = button.DisplayName,
            Purpose = ValueOrNotSpecified(button.Purpose, button.Tooltip),
            HowToUse = [$"Select {button.DisplayName}."],
            ExpectedResult = "Not specified",
            Conditions = NonEmpty(button.VisibilityCondition, button.DisabledCondition),
            Notes = []
        }).ToList(),
        Fields = screen.InputFields.Select(field => new FieldDocumentation
        {
            Name = field.DisplayName,
            Purpose = !string.IsNullOrWhiteSpace(field.Placeholder)
                ? $"Accepts {field.Placeholder}."
                : $"Accepts a value for {field.DisplayName}.",
            Type = field.InputType,
            Required = field.Required,
            Validation = field.ValidationAttributes.Select(pair => $"{pair.Key}: {pair.Value}").ToList(),
            Example = "",
            Notes = []
        }).Concat(screen.Dropdowns.Select(dropdown => new FieldDocumentation
        {
            Name = dropdown.DisplayName,
            Purpose = $"Selects a value for {dropdown.DisplayName}.",
            Type = "dropdown",
            Required = dropdown.Required,
            Validation = [],
            Example = "",
            Notes = []
        })).ToList(),
        Tables = screen.Tables.Select(table => new TableDocumentation
        {
            Name = table.Name,
            Purpose = $"Displays information in {table.Name}.",
            Columns = table.Headers.Select(header => new ColumnDocumentation
            {
                Name = header,
                Description = $"{header} value displayed in the table."
            }).ToList(),
            RowActions = table.ActionColumns,
            Sorting = table.SortableColumns.Count == 0
                ? "Not specified"
                : $"Supports sorting by {string.Join(", ", table.SortableColumns)}.",
            Filtering = table.FilterableColumns.Count == 0
                ? "Not specified"
                : $"Supports filtering by {string.Join(", ", table.FilterableColumns)}.",
            Pagination = ValueOrNotSpecified(table.PaginationInformation)
        }).ToList(),
        Filters = screen.Filters.Select(filter => new ManualSection
        {
            Heading = filter.Label,
            Summary = $"Filters records using {filter.Label}.",
            Steps = [$"Enter or select a value for {filter.Label}."],
            Notes = []
        }).ToList(),
        Tabs = [],
        Procedures = [],
        BestPractices = [],
        Troubleshooting = [],
        Notes = [],
        ChangeSummary = []
    };

    private static string ValueOrNotSpecified(params string[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "Not specified";

    private static List<string> NonEmpty(params string[] values) =>
        values.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).ToList();
}
