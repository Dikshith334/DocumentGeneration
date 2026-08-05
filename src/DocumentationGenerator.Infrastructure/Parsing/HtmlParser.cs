using System.Text.RegularExpressions;
using AngleSharp.Dom;
using DocumentationGenerator.Application.Contracts;
using DocumentationGenerator.Domain.Models;

namespace DocumentationGenerator.Infrastructure.Parsing;

public class HtmlParser : IHtmlParser
{
    protected virtual bool IncludeAngularBindings => false;

    public async Task<Screen> ParseAsync(string html, string screenName, string sourceFileName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(html);
        var parser = new AngleSharp.Html.Parser.HtmlParser();
        var document = await parser.ParseDocumentAsync(html, cancellationToken);
        var screen = new Screen
        {
            Name = Clean(screenName),
            SourceFileName = Path.GetFileName(sourceFileName),
            Description = Clean(document.QuerySelector("main p, .page-description, .lead")?.TextContent ?? string.Empty),
            NavigationPath = ExtractNavigation(document)
        };

        screen.Buttons = ExtractButtons(document);
        screen.InputFields = ExtractInputs(document);
        screen.Dropdowns = ExtractDropdowns(document);
        screen.Filters = ExtractFilters(document, screen.InputFields, screen.Dropdowns);
        screen.Tables = ExtractTables(document, screen.Filters);
        screen.Tabs = ExtractTabs(document);
        screen.Links = ExtractLinks(document);
        screen.Labels = DistinctText(document.QuerySelectorAll("label, legend, mat-label").Select(x => x.TextContent));
        screen.DetectedSections = DistinctText(document.QuerySelectorAll("h1, h2, h3, h4, .card-title, section > header")
            .Select(x => x.TextContent));
        return screen;
    }

    private List<Button> ExtractButtons(IDocument document)
    {
        var elements = document.QuerySelectorAll(
            "button, input[type=button], input[type=submit], input[type=reset], a[role=button], mat-button, [mat-button], [mat-raised-button], [mat-icon-button]");
        var results = elements.Select(element =>
        {
            var icon = Clean(element.QuerySelector("mat-icon, i, .icon")?.TextContent ??
                             element.GetAttribute("data-icon") ?? string.Empty);
            var directText = Clean(string.Join(' ', element.ChildNodes
                .Where(node => !node.NodeName.Equals("MAT-ICON", StringComparison.OrdinalIgnoreCase) &&
                               !(node is IElement child && child.ClassList.Contains("icon")))
                .Select(node => node.TextContent)));
            var value = Clean(element.GetAttribute("value") ?? string.Empty);
            return new Button
            {
                Text = First(directText, value),
                Name = Clean(element.GetAttribute("name") ?? string.Empty),
                ElementId = Clean(element.Id),
                Type = Clean(element.GetAttribute("type") ?? "button"),
                Icon = icon,
                Tooltip = First(Attribute(element, "mattooltip", "data-bs-title", "title")),
                CssClasses = Clean(element.ClassName),
                AngularClickHandler = AngularAttribute(element, "(click)", "on-click"),
                VisibilityCondition = AngularAttribute(element, "*ngif", "[hidden]", "ng-if"),
                DisabledCondition = First(AngularAttribute(element, "[disabled]"),
                    element.HasAttribute("disabled") ? "disabled" : string.Empty),
                AriaLabel = Clean(element.GetAttribute("aria-label") ?? string.Empty),
                SourceSnippet = Truncate(Clean(element.OuterHtml), 260)
            };
        });
        return DistinctBy(results, x => $"{Normalize(x.DisplayName)}|{Normalize(x.AngularClickHandler)}");
    }

    private List<InputField> ExtractInputs(IDocument document)
    {
        var elements = document.QuerySelectorAll(
            "input:not([type=button]):not([type=submit]):not([type=reset]):not([type=hidden]), textarea, mat-checkbox, mat-radio-button, mat-slide-toggle");
        var fields = elements.Select(element =>
        {
            var type = element.LocalName.StartsWith("mat-", StringComparison.OrdinalIgnoreCase)
                ? element.LocalName[4..]
                : element.LocalName.Equals("textarea", StringComparison.OrdinalIgnoreCase)
                    ? "textarea"
                    : element.GetAttribute("type") ?? "text";
            var validation = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in new[] { "min", "max", "minlength", "maxlength", "pattern", "step" })
            {
                var value = element.GetAttribute(name);
                if (!string.IsNullOrWhiteSpace(value)) validation[name] = value;
            }
            return new InputField
            {
                Label = FindLabel(document, element),
                Name = First(Attribute(element, "name", "formcontrolname")),
                ElementId = Clean(element.Id),
                InputType = Clean(type),
                Placeholder = Clean(element.GetAttribute("placeholder") ?? string.Empty),
                Required = element.HasAttribute("required") || element.HasAttribute("[required]") ||
                           element.GetAttribute("aria-required")?.Equals("true", StringComparison.OrdinalIgnoreCase) == true,
                ReadOnly = element.HasAttribute("readonly") || element.HasAttribute("[readonly]"),
                Disabled = element.HasAttribute("disabled"),
                ValidationAttributes = validation,
                AngularModelBinding = AngularAttribute(element, "[(ngmodel)]", "formcontrolname", "[value]"),
                VisibilityCondition = AngularAttribute(element, "*ngif", "[hidden]")
            };
        });
        return DistinctBy(fields, x => $"{Normalize(x.DisplayName)}|{Normalize(x.AngularModelBinding)}");
    }

    private List<Dropdown> ExtractDropdowns(IDocument document)
    {
        var dropdowns = document.QuerySelectorAll("select, mat-select").Select(element => new Dropdown
        {
            Label = FindLabel(document, element),
            Name = First(Attribute(element, "name", "formcontrolname"), Clean(element.Id)),
            Options = DistinctText(element.QuerySelectorAll("option, mat-option").Select(x => x.TextContent)),
            Placeholder = Clean(element.GetAttribute("placeholder") ?? string.Empty),
            Required = element.HasAttribute("required") || element.HasAttribute("[required]"),
            AngularModelBinding = AngularAttribute(element, "[(ngmodel)]", "formcontrolname", "[value]"),
            ChangeHandler = AngularAttribute(element, "(change)", "(selectionchange)"),
            VisibilityCondition = AngularAttribute(element, "*ngif", "[hidden]")
        });
        return DistinctBy(dropdowns, x => $"{Normalize(x.DisplayName)}|{Normalize(x.AngularModelBinding)}");
    }

    private static List<FilterDefinition> ExtractFilters(IDocument document,
        IEnumerable<InputField> fields, IEnumerable<Dropdown> dropdowns)
    {
        var filters = new List<FilterDefinition>();
        foreach (var field in fields)
        {
            var key = $"{field.Label} {field.Name} {field.Placeholder} {field.InputType}";
            var source = FindElement(document, field.ElementId, field.Name);
            if (!IsFilter(key, source)) continue;
            filters.Add(new FilterDefinition
            {
                Label = field.DisplayName,
                Type = field.InputType,
                Placeholder = field.Placeholder,
                TargetField = field.Name
            });
        }
        foreach (var dropdown in dropdowns)
        {
            var key = $"{dropdown.Label} {dropdown.Name} {dropdown.Placeholder}";
            if (!IsFilter(key, null) && !key.Contains("status", StringComparison.OrdinalIgnoreCase)) continue;
            filters.Add(new FilterDefinition
            {
                Label = dropdown.DisplayName,
                Type = "dropdown",
                Placeholder = dropdown.Placeholder,
                TargetField = dropdown.Name,
                AvailableOptions = dropdown.Options
            });
        }
        return DistinctBy(filters, x => Normalize(x.Label));
    }

    private static List<TableDefinition> ExtractTables(IDocument document, IReadOnlyCollection<FilterDefinition> filters)
    {
        var paginator = document.All.FirstOrDefault(IsPaginator);
        var tables = document.QuerySelectorAll("table, mat-table, [mat-table]").Select((element, index) =>
        {
            var headers = DistinctText(element.QuerySelectorAll("th, [mat-header-cell], mat-header-cell")
                .Select(x => First(Clean(x.TextContent), Clean(x.GetAttribute("mat-column-def") ?? string.Empty))));
            var sortable = DistinctText(element.QuerySelectorAll("th[mat-sort-header], [mat-sort-header], th[aria-sort]")
                .Select(x => Clean(x.TextContent)));
            var emptyText = Clean(element.QuerySelector(".empty-state, .no-data, [matnodatarow]")?.TextContent ?? string.Empty);
            return new TableDefinition
            {
                Name = First(Clean(element.GetAttribute("aria-label") ?? string.Empty), Clean(element.Id), $"Table {index + 1}"),
                Headers = headers,
                ActionColumns = headers.Where(x => x.Contains("action", StringComparison.OrdinalIgnoreCase)).ToList(),
                SortableColumns = sortable,
                FilterableColumns = headers.Where(header => filters.Any(filter =>
                    Normalize(filter.TargetField).Contains(Normalize(header), StringComparison.Ordinal) ||
                    Normalize(filter.Label).Contains(Normalize(header), StringComparison.Ordinal))).ToList(),
                PaginationInformation = paginator is null ? string.Empty : Clean(paginator.TextContent + " " + paginator.GetAttribute("[pageSizeOptions]")),
                EmptyStateText = emptyText
            };
        });
        return DistinctBy(tables, x => $"{Normalize(x.Name)}|{string.Join('|', x.Headers.Select(Normalize))}");
    }

    private List<TabDefinition> ExtractTabs(IDocument document)
    {
        var tabs = document.QuerySelectorAll("mat-tab, [role=tab], .nav-tabs .nav-link").Select(element => new TabDefinition
        {
            Title = First(Clean(element.GetAttribute("label") ?? string.Empty), Clean(element.TextContent)),
            Identifier = First(Clean(element.Id), Clean(element.GetAttribute("aria-controls") ?? string.Empty)),
            VisibilityCondition = AngularAttribute(element, "*ngif", "[hidden]"),
            ActiveCondition = First(AngularAttribute(element, "[active]", "[selectedindex]"),
                element.ClassList.Contains("active") ? "active" : string.Empty)
        }).Where(x => !string.IsNullOrWhiteSpace(x.Title));
        return DistinctBy(tabs, x => Normalize(x.Title));
    }

    private static List<LinkDefinition> ExtractLinks(IDocument document) => DistinctBy(
        document.QuerySelectorAll("a[href], a[routerlink]")
            .Where(x => !x.GetAttribute("role")?.Equals("button", StringComparison.OrdinalIgnoreCase) == true)
            .Select(x => new LinkDefinition
            {
                Text = Clean(x.TextContent),
                Href = Clean(x.GetAttribute("href") ?? string.Empty),
                AngularRoute = First(Attribute(x, "routerlink", "[routerlink]"))
            }).Where(x => !string.IsNullOrWhiteSpace(x.Text)),
        x => $"{Normalize(x.Text)}|{Normalize(x.Href)}|{Normalize(x.AngularRoute)}");

    private static string FindLabel(IDocument document, IElement element)
    {
        if (!string.IsNullOrWhiteSpace(element.Id))
        {
            var label = document.QuerySelectorAll("label")
                .FirstOrDefault(x => x.GetAttribute("for")?.Equals(element.Id, StringComparison.Ordinal) == true);
            if (label is not null) return Clean(label.TextContent);
        }
        for (var parent = element.ParentElement; parent is not null; parent = parent.ParentElement)
        {
            var label = parent.QuerySelector("mat-label, label, legend");
            if (label is not null) return Clean(label.TextContent);
            if (parent.LocalName is "form" or "body") break;
        }
        return First(Clean(element.GetAttribute("aria-label") ?? string.Empty),
            Clean(element.GetAttribute("placeholder") ?? string.Empty));
    }

    private string AngularAttribute(IElement element, params string[] names) =>
        IncludeAngularBindings ? First(Attribute(element, names)) : string.Empty;

    private static string Attribute(IElement element, params string[] names)
    {
        foreach (var name in names)
        {
            var attribute = element.Attributes.FirstOrDefault(x =>
                x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (attribute is not null && !string.IsNullOrWhiteSpace(attribute.Value)) return Clean(attribute.Value);
        }
        return string.Empty;
    }

    private static string ExtractNavigation(IDocument document)
    {
        var breadcrumb = document.QuerySelector(".breadcrumb, nav[aria-label=breadcrumb]");
        return breadcrumb is null
            ? string.Empty
            : string.Join(" > ", DistinctText(breadcrumb.QuerySelectorAll("a, li, .breadcrumb-item").Select(x => x.TextContent)));
    }

    private static IElement? FindElement(IDocument document, string id, string name) => document.All.FirstOrDefault(x =>
        (!string.IsNullOrWhiteSpace(id) && string.Equals(x.Id, id, StringComparison.Ordinal)) ||
        (!string.IsNullOrWhiteSpace(name) && x.GetAttribute("name")?.Equals(name, StringComparison.OrdinalIgnoreCase) == true));

    private static bool IsFilter(string key, IElement? element)
    {
        if (Regex.IsMatch(key, "search|filter|query", RegexOptions.IgnoreCase)) return true;
        for (var parent = element; parent is not null; parent = parent.ParentElement)
        {
            if ((parent.ClassName ?? string.Empty).Contains("filter", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static bool IsPaginator(IElement element) =>
        element.LocalName.Equals("mat-paginator", StringComparison.OrdinalIgnoreCase) ||
        element.ClassList.Any(x => x.Contains("pagination", StringComparison.OrdinalIgnoreCase)) ||
        (element.GetAttribute("aria-label")?.Contains("pagination", StringComparison.OrdinalIgnoreCase) ?? false);

    protected static string Clean(string? value) => Regex.Replace(value ?? string.Empty, @"\s+", " ").Trim();
    private static string Normalize(string value) => Regex.Replace(Clean(value).ToLowerInvariant(), @"[^a-z0-9]+", string.Empty);
    private static string First(params string[] values) => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty;
    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..length] + "...";

    private static List<string> DistinctText(IEnumerable<string> values) => values.Select(Clean)
        .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private static List<T> DistinctBy<T>(IEnumerable<T> values, Func<T, string> keySelector) => values
        .Where(x => !string.IsNullOrWhiteSpace(keySelector(x)))
        .GroupBy(keySelector, StringComparer.OrdinalIgnoreCase).Select(group => group.First()).ToList();
}
