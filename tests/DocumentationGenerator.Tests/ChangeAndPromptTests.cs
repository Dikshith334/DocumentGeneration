using DocumentationGenerator.Application.Services;
using DocumentationGenerator.Domain.Models;
using DocumentationGenerator.Infrastructure.Analysis;

namespace DocumentationGenerator.Tests;

public sealed class ChangeAndPromptTests
{
    [Fact]
    public void Detects_Added_And_Possibly_Removed_Items()
    {
        var screen = new Screen
        {
            Name = "Customers",
            Buttons = [new Button { Text = "Search" }, new Button { Text = "Export" }]
        };
        var manual = new ExistingManual
        {
            PlainText = "Feature | Description\nSearch | Finds customers\nReset Filter | Clears filters",
            Tables =
            [
                new ManualTable
                {
                    Rows =
                    [
                        ["Feature", "Description"],
                        ["Search", "Finds customers"],
                        ["Reset Filter", "Clears filters"]
                    ]
                }
            ]
        };

        var changes = new DocumentationChangeDetector().Detect(screen, manual);

        Assert.Contains(changes.AddedElements, item => item.Name == "Export");
        Assert.Contains(changes.ExistingElements, item => item.Name == "Search");
        Assert.Contains(changes.PossiblyRemovedElements, item => item.Name == "Reset Filter" && item.RequiresReview);
    }

    [Fact]
    public void Prompt_Puts_Business_Rules_First_And_Contains_Anti_Hallucination_Rules()
    {
        var screen = new Screen
        {
            Name = "Customers",
            SourceFileName = "customers.html",
            BusinessRules = "Delete is available only to Admin users.",
            Buttons = [new Button { Text = "Delete", VisibilityCondition = "isAdmin" }]
        };

        var prompt = new PromptBuilder().Build(screen, null, null, new DocumentationChangeSet());

        Assert.Contains("highest priority", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Delete is available only to Admin users.", prompt);
        Assert.Contains("Do not invent behavior", prompt);
        Assert.Contains("strict JSON", prompt);
        Assert.DoesNotContain("Source-supported", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Select Delete.", prompt);
        Assert.True(prompt.IndexOf("EXPLICIT BUSINESS RULES", StringComparison.Ordinal) <
                    prompt.IndexOf("EXTRACTED SCREEN MODEL", StringComparison.Ordinal));
    }

    [Fact]
    public void Manual_Completer_Restores_Content_When_Ollama_Returns_Only_A_Title()
    {
        var analysis = new AnalysisResult
        {
            Screen = new Screen
            {
                Name = "Customers",
                Description = "Find and maintain customer records.",
                NavigationPath = "Operations > Customers",
                BusinessRules = "Delete is available only to Admin users.",
                Buttons =
                [
                    new Button { Text = "Search", Tooltip = "Apply search", AngularClickHandler = "searchCustomers()" },
                    new Button { AriaLabel = "Delete", Tooltip = "Delete customer", AngularClickHandler = "deleteCustomer(customer)", VisibilityCondition = "currentUser.isAdmin" }
                ],
                InputFields = [new InputField { Label = "Search", InputType = "search", Placeholder = "Customer name or email" }],
                Tables =
                [
                    new TableDefinition
                    {
                        Name = "Customer table", Headers = ["Customer ID", "Actions"],
                        ActionColumns = ["Actions"], SortableColumns = ["Customer ID"],
                        PaginationInformation = "Page sizes: 10, 25"
                    }
                ],
                Filters = [new FilterDefinition { Label = "Search", Type = "search" }]
            },
            Changes = new DocumentationChangeSet { Summary = "New controls were detected." }
        };
        var manual = new UserManual { Title = "Customers User Manual" };

        ManualCompleter.Complete(manual, analysis);

        Assert.Equal("Find and maintain customer records.", manual.Overview);
        Assert.Equal("Operations > Customers", manual.Navigation);
        Assert.Equal(2, manual.Buttons.Count);
        Assert.Equal("Apply search.", manual.Buttons[0].Purpose);
        Assert.Contains("Delete is available only to Admin users.", manual.Buttons[1].Conditions);
        Assert.Single(manual.Fields);
        Assert.Single(manual.Tables);
        Assert.Equal(2, manual.Tables[0].Columns.Count);
        Assert.Equal("Supports sorting by Customer ID.", manual.Tables[0].Sorting);
        Assert.Single(manual.Filters);
        Assert.DoesNotContain("Source-supported", System.Text.Json.JsonSerializer.Serialize(manual),
            StringComparison.OrdinalIgnoreCase);
    }
}
