using System.Text.RegularExpressions;
using DocumentationGenerator.Application.Contracts;
using DocumentationGenerator.Domain.Models;

namespace DocumentationGenerator.Infrastructure.Analysis;

public sealed class DocumentationChangeDetector : IDocumentationChangeDetector
{
    public DocumentationChangeSet Detect(Screen screen, ExistingManual? manual)
    {
        var result = new DocumentationChangeSet();
        var uiElements = EnumerateUiElements(screen).ToList();
        if (manual is null || string.IsNullOrWhiteSpace(manual.PlainText))
        {
            result.AddedElements = uiElements.Select(ToAdded).ToList();
            result.UndocumentedElements = result.AddedElements.Select(Clone).ToList();
            result.Summary = $"No existing manual was supplied. {uiElements.Count} extracted UI items require documentation.";
            return result;
        }

        var manualText = Normalize(manual.PlainText);
        var manualCandidates = ExtractManualCandidates(manual).ToList();
        foreach (var element in uiElements)
        {
            if (ContainsPhrase(manualText, element.Name))
            {
                result.ExistingElements.Add(new DocumentationElement
                {
                    Category = element.Category,
                    Name = element.Name,
                    Evidence = "Found in both the current UI and existing manual."
                });
                continue;
            }

            var possibleMatch = manualCandidates
                .Select(candidate => new { Candidate = candidate, Score = Similarity(element.Name, candidate) })
                .OrderByDescending(x => x.Score)
                .FirstOrDefault();
            if (possibleMatch is { Score: >= 0.58 })
            {
                result.PossiblyChangedElements.Add(new DocumentationElement
                {
                    Category = element.Category,
                    Name = element.Name,
                    PossibleMatch = possibleMatch.Candidate,
                    Evidence = "Similar wording may indicate a renamed item.",
                    RequiresReview = true
                });
            }
            else
            {
                var added = ToAdded(element);
                result.AddedElements.Add(added);
                result.UndocumentedElements.Add(Clone(added));
            }
        }

        foreach (var candidate in manualCandidates)
        {
            if (uiElements.Any(element => Similarity(element.Name, candidate) >= 0.58)) continue;
            result.PossiblyRemovedElements.Add(new DocumentationElement
            {
                Category = "Existing manual item",
                Name = candidate,
                Evidence = "Mentioned in the existing manual but not confidently matched to static HTML.",
                RequiresReview = true
            });
        }

        result.Summary = $"{result.AddedElements.Count} added, {result.ExistingElements.Count} existing, " +
                         $"{result.PossiblyChangedElements.Count} possibly renamed, and " +
                         $"{result.PossiblyRemovedElements.Count} possibly removed items. Review uncertain matches because runtime UI is not visible in static HTML.";
        return result;
    }

    private static IEnumerable<(string Category, string Name)> EnumerateUiElements(Screen screen)
    {
        foreach (var button in screen.Buttons) yield return ("Button", button.DisplayName);
        foreach (var field in screen.InputFields) yield return ("Field", field.DisplayName);
        foreach (var dropdown in screen.Dropdowns) yield return ("Dropdown", dropdown.DisplayName);
        foreach (var filter in screen.Filters) yield return ("Filter", filter.Label);
        foreach (var table in screen.Tables)
        {
            yield return ("Table", table.Name);
            foreach (var header in table.Headers) yield return ("Table column", header);
        }
        foreach (var tab in screen.Tabs) yield return ("Tab", tab.Title);
    }

    private static IEnumerable<string> ExtractManualCandidates(ExistingManual manual)
    {
        var candidates = new List<string>();
        foreach (var table in manual.Tables)
        {
            foreach (var row in table.Rows.Skip(1))
            {
                var cell = row.Count > 1 && Regex.IsMatch(row[0], @"^\d+$") ? row[1] : row.FirstOrDefault();
                if (IsCandidate(cell)) candidates.Add(cell!);
            }
        }
        foreach (var heading in manual.Headings)
        {
            var value = Regex.Replace(heading, @"^\d+(\.\d+)*[.)]?\s*", string.Empty).Trim();
            if (IsCandidate(value)) candidates.Add(value);
        }
        return candidates.Distinct(StringComparer.OrdinalIgnoreCase).Take(200);
    }

    private static bool IsCandidate(string? value) => !string.IsNullOrWhiteSpace(value) &&
        value.Length is >= 2 and <= 80 &&
        value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 10 &&
        !Regex.IsMatch(value, "^(no|number|description|feature|contents|overview)$", RegexOptions.IgnoreCase);

    private static bool ContainsPhrase(string normalizedManual, string value)
    {
        var normalizedValue = Normalize(value);
        return normalizedValue.Length >= 2 && normalizedManual.Contains(normalizedValue, StringComparison.Ordinal);
    }

    private static string Normalize(string value) => Regex.Replace(value.ToLowerInvariant(), @"[^a-z0-9]+", string.Empty);

    private static double Similarity(string left, string right)
    {
        var a = Normalize(left);
        var b = Normalize(right);
        if (a.Length == 0 || b.Length == 0) return 0;
        if (a.Contains(b, StringComparison.Ordinal) || b.Contains(a, StringComparison.Ordinal))
        {
            return (double)Math.Min(a.Length, b.Length) / Math.Max(a.Length, b.Length);
        }
        var distance = Levenshtein(a, b);
        return 1d - (double)distance / Math.Max(a.Length, b.Length);
    }

    private static int Levenshtein(string left, string right)
    {
        var previous = Enumerable.Range(0, right.Length + 1).ToArray();
        for (var i = 1; i <= left.Length; i++)
        {
            var current = new int[right.Length + 1];
            current[0] = i;
            for (var j = 1; j <= right.Length; j++)
            {
                var cost = left[i - 1] == right[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }
            previous = current;
        }
        return previous[right.Length];
    }

    private static DocumentationElement ToAdded((string Category, string Name) element) => new()
    {
        Category = element.Category,
        Name = element.Name,
        Evidence = "Present in the current HTML but not found in the existing manual."
    };

    private static DocumentationElement Clone(DocumentationElement source) => new()
    {
        Category = source.Category,
        Name = source.Name,
        Evidence = source.Evidence,
        PossibleMatch = source.PossibleMatch,
        RequiresReview = source.RequiresReview
    };
}
