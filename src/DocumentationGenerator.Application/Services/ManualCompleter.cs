using DocumentationGenerator.Domain.Models;

namespace DocumentationGenerator.Application.Services;

/// <summary>
/// Merges AI output with deterministic facts extracted from source files, ensuring that a
/// minimal or malformed model response cannot remove documented UI elements.
/// </summary>
public static class ManualCompleter
{
    public static void Complete(UserManual manual, AnalysisResult analysis)
    {
        ArgumentNullException.ThrowIfNull(manual);
        ArgumentNullException.ThrowIfNull(analysis);
        var screen = analysis.Screen;

        manual.Title = Choose(manual.Title, $"{screen.Name} User Manual");
        manual.Overview = Choose(manual.Overview, screen.Description);
        manual.Navigation = Choose(manual.Navigation, screen.NavigationPath);
        manual.ScreenOverview = Choose(manual.ScreenOverview, screen.Description,
            screen.DetectedSections.Count == 0
                ? null
                : $"The screen includes {string.Join(", ", screen.DetectedSections)}.");
        manual.Prerequisites = CleanList(manual.Prerequisites, "supplied prerequisite");

        manual.Buttons.RemoveAll(item => IsTemplatePlaceholder(item.Name));
        foreach (var source in screen.Buttons)
        {
            var item = manual.Buttons.FirstOrDefault(candidate => Same(candidate.Name, source.DisplayName));
            if (item is null)
            {
                item = new ButtonDocumentation { Name = source.DisplayName };
                manual.Buttons.Add(item);
            }
            CompleteButton(item, source, screen.BusinessRules);
        }

        manual.Fields.RemoveAll(item => IsTemplatePlaceholder(item.Name));
        foreach (var source in screen.InputFields)
        {
            var item = manual.Fields.FirstOrDefault(candidate => Same(candidate.Name, source.DisplayName));
            if (item is null)
            {
                item = new FieldDocumentation { Name = source.DisplayName };
                manual.Fields.Add(item);
            }
            item.Purpose = Choose(item.Purpose,
                !string.IsNullOrWhiteSpace(source.Placeholder)
                    ? $"Accepts {source.Placeholder}."
                    : $"Accepts a value for {source.DisplayName}.");
            item.Type = Choose(item.Type, source.InputType);
            item.Required |= source.Required;
            if (item.Validation.Count == 0)
                item.Validation = source.ValidationAttributes.Select(pair => $"{pair.Key}: {pair.Value}").ToList();
            item.Validation = CleanList(item.Validation);
            item.Notes = CleanList(item.Notes);
        }
        foreach (var source in screen.Dropdowns)
        {
            var item = manual.Fields.FirstOrDefault(candidate => Same(candidate.Name, source.DisplayName));
            if (item is null)
            {
                item = new FieldDocumentation { Name = source.DisplayName };
                manual.Fields.Add(item);
            }
            item.Purpose = Choose(item.Purpose, $"Selects a value for {source.DisplayName}.");
            item.Type = Choose(item.Type, "dropdown");
            item.Required |= source.Required;
            item.Notes = CleanList(item.Notes);
        }

        manual.Tables.RemoveAll(item => IsTemplatePlaceholder(item.Name));
        foreach (var source in screen.Tables)
        {
            var item = manual.Tables.FirstOrDefault(candidate => Same(candidate.Name, source.Name));
            if (item is null)
            {
                item = new TableDocumentation { Name = source.Name };
                manual.Tables.Add(item);
            }
            CompleteTable(item, source, screen);
        }

        manual.Filters.RemoveAll(item => IsTemplatePlaceholder(item.Heading));
        foreach (var source in screen.Filters)
        {
            var item = manual.Filters.FirstOrDefault(candidate => Same(candidate.Heading, source.Label));
            if (item is null)
            {
                item = new ManualSection { Heading = source.Label };
                manual.Filters.Add(item);
            }
            item.Summary = Choose(item.Summary, $"Filters records using {source.Label}.");
            if (item.Steps.Count == 0 || item.Steps.Any(IsTemplatePlaceholder))
                item.Steps = [$"Enter or select a value for {source.Label}."];
            item.Notes = CleanList(item.Notes);
        }

        foreach (var section in manual.Filters.Concat(manual.Tabs).Concat(manual.Procedures))
        {
            section.Summary = Choose(section.Summary);
            section.Steps = CleanList(section.Steps);
            section.Notes = CleanList(section.Notes);
        }
        manual.BestPractices = CleanList(manual.BestPractices);
        manual.Notes = CleanList(manual.Notes);
        if (manual.ChangeSummary.Count == 0 && !string.IsNullOrWhiteSpace(analysis.Changes.Summary))
            manual.ChangeSummary.Add(analysis.Changes.Summary);
        manual.ChangeSummary = CleanList(manual.ChangeSummary);
    }

    private static void CompleteButton(ButtonDocumentation item, Button source, string businessRules)
    {
        item.Name = Choose(item.Name, source.DisplayName);
        item.Purpose = Choose(item.Purpose, Sentence(source.Purpose), Sentence(source.Tooltip));
        if (item.HowToUse.Count == 0 || item.HowToUse.Any(IsTemplatePlaceholder))
            item.HowToUse = [$"Select {source.DisplayName}."];
        item.HowToUse = CleanList(item.HowToUse);
        item.ExpectedResult = Choose(item.ExpectedResult, $"The {source.DisplayName} action is requested.");

        item.Conditions = CleanList(item.Conditions);
        if (item.Conditions.Count == 0)
        {
            item.Conditions.AddRange(MatchingRules(businessRules, source.DisplayName));
            if (!string.IsNullOrWhiteSpace(source.VisibilityCondition))
                item.Conditions.Add($"Available when {source.VisibilityCondition}.");
            if (!string.IsNullOrWhiteSpace(source.DisabledCondition))
                item.Conditions.Add($"Unavailable when {source.DisabledCondition}.");
        }
        item.Conditions = item.Conditions.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        item.Notes = CleanList(item.Notes);
    }

    private static void CompleteTable(TableDocumentation item, TableDefinition source, Screen screen)
    {
        item.Name = Choose(item.Name, source.Name);
        item.Purpose = Choose(item.Purpose, $"Displays information in {source.Name}.");
        item.Columns.RemoveAll(column => IsTemplatePlaceholder(column.Name));
        foreach (var header in source.Headers)
        {
            var column = item.Columns.FirstOrDefault(candidate => Same(candidate.Name, header));
            if (column is null)
            {
                column = new ColumnDocumentation { Name = header };
                item.Columns.Add(column);
            }
            column.Description = Choose(column.Description, $"{header} value displayed in the table.");
        }

        item.RowActions = CleanList(item.RowActions);
        if (item.RowActions.Count == 0 && source.ActionColumns.Count > 0)
        {
            item.RowActions = screen.Buttons
                .Where(button => StartsWithAction(button.AngularClickHandler))
                .Select(button => button.DisplayName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        item.Sorting = Choose(item.Sorting, source.SortableColumns.Count == 0
            ? null
            : $"Supports sorting by {string.Join(", ", source.SortableColumns)}.");
        item.Filtering = Choose(item.Filtering, source.FilterableColumns.Count == 0
            ? null
            : $"Supports filtering by {string.Join(", ", source.FilterableColumns)}.");
        item.Pagination = Choose(item.Pagination, source.PaginationInformation);
    }

    private static bool StartsWithAction(string handler) =>
        handler.StartsWith("edit", StringComparison.OrdinalIgnoreCase) ||
        handler.StartsWith("delete", StringComparison.OrdinalIgnoreCase) ||
        handler.StartsWith("view", StringComparison.OrdinalIgnoreCase) ||
        handler.StartsWith("open", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> MatchingRules(string rules, string name) =>
        rules.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(rule => rule.Contains(name, StringComparison.OrdinalIgnoreCase));

    private static string Choose(string? current, params string?[] fallbacks)
    {
        if (!string.IsNullOrWhiteSpace(current) &&
            !current.Equals("Not specified", StringComparison.OrdinalIgnoreCase) &&
            !IsTemplatePlaceholder(current)) return current.Trim();
        return fallbacks.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "Not specified";
    }

    private static string? Sentence(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value.Trim();
        return text.EndsWith('.') ? text : text + ".";
    }

    private static List<string> CleanList(IEnumerable<string>? values, string? extraPlaceholder = null) =>
        (values ?? []).Where(value => !string.IsNullOrWhiteSpace(value) &&
                                     !IsTemplatePlaceholder(value) &&
                                     (extraPlaceholder is null ||
                                      !value.Contains(extraPlaceholder, StringComparison.OrdinalIgnoreCase)))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static bool Same(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right) &&
        left.Trim().Equals(right.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool IsTemplatePlaceholder(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        (value.Contains("source-supported", StringComparison.OrdinalIgnoreCase) ||
         value.Equals("Button name", StringComparison.OrdinalIgnoreCase) ||
         value.Equals("Field name", StringComparison.OrdinalIgnoreCase) ||
         value.Equals("Table name", StringComparison.OrdinalIgnoreCase) ||
         value.Equals("Column name", StringComparison.OrdinalIgnoreCase) ||
         value.Equals("Filter name", StringComparison.OrdinalIgnoreCase));
}
