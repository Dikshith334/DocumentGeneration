using System.Text;
using DocumentationGenerator.Application.Contracts;
using DocumentationGenerator.Domain.Models;

namespace DocumentationGenerator.Infrastructure.Export;

public sealed class MarkdownExporter : IMarkdownExporter
{
    public async Task ExportAsync(UserManual manual, string outputPath,
        CancellationToken cancellationToken = default)
    {
        var markdown = new StringBuilder();
        markdown.AppendLine($"# {manual.Title}").AppendLine();
        markdown.AppendLine($"_Generated {manual.GeneratedDate:dd MMMM yyyy}_").AppendLine();
        AddTextSection(markdown, "Overview", manual.Overview);
        AddTextSection(markdown, "Navigation", manual.Navigation);
        AddTextSection(markdown, "Screen overview", manual.ScreenOverview);
        AddListSection(markdown, "Prerequisites", manual.Prerequisites);

        if (manual.Buttons.Count > 0)
        {
            markdown.AppendLine("## Feature index").AppendLine();
            markdown.AppendLine("| No. | Feature | Description |");
            markdown.AppendLine("| ---: | --- | --- |");
            for (var index = 0; index < manual.Buttons.Count; index++)
            {
                var button = manual.Buttons[index];
                markdown.AppendLine($"| {index + 1} | {Escape(button.Name)} | {Escape(button.Purpose)} |");
            }
            markdown.AppendLine();
            for (var index = 0; index < manual.Buttons.Count; index++) AddButton(markdown, manual.Buttons[index], index + 1);
        }

        if (manual.Fields.Count > 0)
        {
            markdown.AppendLine("## Fields").AppendLine();
            markdown.AppendLine("| Field | Purpose | Type | Required | Validation |");
            markdown.AppendLine("| --- | --- | --- | :---: | --- |");
            foreach (var field in manual.Fields)
            {
                markdown.AppendLine($"| {Escape(field.Name)} | {Escape(field.Purpose)} | {Escape(field.Type)} | " +
                                    $"{(field.Required ? "Yes" : "No")} | {Escape(string.Join("; ", field.Validation))} |");
            }
            markdown.AppendLine();
        }

        foreach (var table in manual.Tables) AddTableSection(markdown, table);
        AddManualSections(markdown, "Filters", manual.Filters);
        AddManualSections(markdown, "Tabs", manual.Tabs);
        AddManualSections(markdown, "Common procedures", manual.Procedures);
        AddListSection(markdown, "Best practices", manual.BestPractices);

        if (manual.Troubleshooting.Count > 0)
        {
            markdown.AppendLine("## Troubleshooting").AppendLine();
            markdown.AppendLine("| Problem | Possible cause | Solution |");
            markdown.AppendLine("| --- | --- | --- |");
            foreach (var item in manual.Troubleshooting)
            {
                markdown.AppendLine($"| {Escape(item.Problem)} | {Escape(item.PossibleCause)} | {Escape(item.Solution)} |");
            }
            markdown.AppendLine();
        }

        AddListSection(markdown, "Notes", manual.Notes);
        AddListSection(markdown, "Documentation change summary", manual.ChangeSummary);
        if (manual.SourceInformation.Count > 0)
        {
            markdown.AppendLine("## Source information").AppendLine();
            foreach (var source in manual.SourceInformation)
            {
                markdown.AppendLine($"- **{source.SourceType}:** {source.FileName} - {source.Summary}");
            }
            markdown.AppendLine();
        }

        await File.WriteAllTextAsync(outputPath, markdown.ToString(), new UTF8Encoding(false), cancellationToken);
    }

    private static void AddButton(StringBuilder markdown, ButtonDocumentation button, int number)
    {
        markdown.AppendLine($"## {number}. {button.Name}").AppendLine();
        if (!string.IsNullOrWhiteSpace(button.Purpose)) markdown.AppendLine(button.Purpose).AppendLine();
        AddNumbered(markdown, "How to use", button.HowToUse);
        AddTextSection(markdown, "Expected result", button.ExpectedResult, 3);
        AddListSection(markdown, "Conditions", button.Conditions, 3);
        AddListSection(markdown, "Notes and limitations", button.Notes, 3);
    }

    private static void AddTableSection(StringBuilder markdown, TableDocumentation table)
    {
        markdown.AppendLine($"## {table.Name}").AppendLine();
        if (!string.IsNullOrWhiteSpace(table.Purpose)) markdown.AppendLine(table.Purpose).AppendLine();
        if (table.Columns.Count > 0)
        {
            markdown.AppendLine("| Column | Description |");
            markdown.AppendLine("| --- | --- |");
            foreach (var column in table.Columns)
                markdown.AppendLine($"| {Escape(column.Name)} | {Escape(column.Description)} |");
            markdown.AppendLine();
        }
        AddListSection(markdown, "Row actions", table.RowActions, 3);
        AddTextSection(markdown, "Sorting", table.Sorting, 3);
        AddTextSection(markdown, "Filtering", table.Filtering, 3);
        AddTextSection(markdown, "Pagination", table.Pagination, 3);
    }

    private static void AddManualSections(StringBuilder markdown, string title, IReadOnlyCollection<ManualSection> sections)
    {
        if (sections.Count == 0) return;
        markdown.AppendLine($"## {title}").AppendLine();
        foreach (var section in sections)
        {
            markdown.AppendLine($"### {section.Heading}").AppendLine();
            if (!string.IsNullOrWhiteSpace(section.Summary)) markdown.AppendLine(section.Summary).AppendLine();
            AddNumbered(markdown, "Steps", section.Steps, 4);
            AddListSection(markdown, "Notes", section.Notes, 4);
        }
    }

    private static void AddTextSection(StringBuilder markdown, string title, string value, int level = 2)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        markdown.AppendLine($"{new string('#', level)} {title}").AppendLine();
        markdown.AppendLine(value.Trim()).AppendLine();
    }

    private static void AddListSection(StringBuilder markdown, string title, IReadOnlyCollection<string> items, int level = 2)
    {
        if (items.Count == 0) return;
        markdown.AppendLine($"{new string('#', level)} {title}").AppendLine();
        foreach (var item in items.Where(x => !string.IsNullOrWhiteSpace(x))) markdown.AppendLine($"- {item.Trim()}");
        markdown.AppendLine();
    }

    private static void AddNumbered(StringBuilder markdown, string title, IReadOnlyCollection<string> items, int level = 3)
    {
        if (items.Count == 0) return;
        markdown.AppendLine($"{new string('#', level)} {title}").AppendLine();
        var index = 1;
        foreach (var item in items.Where(x => !string.IsNullOrWhiteSpace(x))) markdown.AppendLine($"{index++}. {item.Trim()}");
        markdown.AppendLine();
    }

    private static string Escape(string value) => (value ?? string.Empty).Replace("|", "\\|", StringComparison.Ordinal)
        .Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
}
