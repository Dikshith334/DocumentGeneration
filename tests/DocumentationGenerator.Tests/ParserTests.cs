using DocumentationGenerator.Infrastructure.Parsing;

namespace DocumentationGenerator.Tests;

public sealed class ParserTests
{
    private const string Html = """
        <main>
          <h1>Customer Management</h1>
          <mat-form-field class="filter-panel">
            <mat-label>Search</mat-label>
            <input id="search" type="search" placeholder="Customer name" [(ngModel)]="filters.search" required>
          </mat-form-field>
          <button (click)="addCustomer()" matTooltip="Add a customer"><mat-icon>add</mat-icon>Add</button>
          <button (click)="deleteCustomer(row)" *ngIf="currentUser.isAdmin" aria-label="Delete"><mat-icon>delete</mat-icon></button>
          <table aria-label="Customer table"><thead><tr><th mat-sort-header>Customer ID</th><th>Customer Name</th><th>Actions</th></tr></thead></table>
          <mat-paginator [pageSizeOptions]="[10,25]"></mat-paginator>
        </main>
        """;

    [Fact]
    public async Task Extracts_Angular_Buttons_Handlers_And_Visibility()
    {
        var screen = await new AngularParser().ParseAsync(Html, "Customer Management", "screen.html");

        Assert.Contains(screen.Buttons, button => button.DisplayName == "Add");
        Assert.Contains(screen.Buttons, button => button.AngularClickHandler == "addCustomer()");
        var delete = Assert.Single(screen.Buttons, button => button.DisplayName == "Delete");
        Assert.Equal("currentUser.isAdmin", delete.VisibilityCondition);
    }

    [Fact]
    public async Task Extracts_Input_Label_Binding_And_Filter()
    {
        var screen = await new AngularParser().ParseAsync(Html, "Customer Management", "screen.html");

        var field = Assert.Single(screen.InputFields);
        Assert.Equal("Search", field.Label);
        Assert.Equal("filters.search", field.AngularModelBinding);
        Assert.True(field.Required);
        Assert.Contains(screen.Filters, filter => filter.Label == "Search");
    }

    [Fact]
    public async Task Extracts_Table_Headers_And_Pagination()
    {
        var screen = await new AngularParser().ParseAsync(Html, "Customer Management", "screen.html");

        var table = Assert.Single(screen.Tables);
        Assert.Equal(["Customer ID", "Customer Name", "Actions"], table.Headers);
        Assert.Contains("Customer ID", table.SortableColumns);
        Assert.Contains("10,25", table.PaginationInformation);
    }
}
