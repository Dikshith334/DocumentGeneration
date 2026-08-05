using DocumentationGenerator.Application.Contracts;

namespace DocumentationGenerator.Infrastructure.Parsing;

public sealed class AngularParser : HtmlParser, IAngularParser
{
    protected override bool IncludeAngularBindings => true;
}
