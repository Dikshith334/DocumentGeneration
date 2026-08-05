using DocumentationGenerator.Application.Configuration;
using DocumentationGenerator.Application.Contracts;
using DocumentationGenerator.Application.Services;
using DocumentationGenerator.Infrastructure.Analysis;
using DocumentationGenerator.Infrastructure.Documents;
using DocumentationGenerator.Infrastructure.Export;
using DocumentationGenerator.Infrastructure.Ollama;
using DocumentationGenerator.Infrastructure.Parsing;
using DocumentationGenerator.Infrastructure.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DocumentationGenerator.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddDocumentationGenerator(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<OllamaOptions>().Bind(configuration.GetSection(OllamaOptions.SectionName))
            .Validate(options => Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _), "Ollama BaseUrl must be absolute.")
            .Validate(options => options.TimeoutSeconds is > 0 and <= 1800, "Ollama timeout must be between 1 and 1800 seconds.")
            .ValidateOnStart();
        services.AddOptions<StorageOptions>().Bind(configuration.GetSection(StorageOptions.SectionName)).ValidateOnStart();
        services.AddOptions<UploadOptions>().Bind(configuration.GetSection(UploadOptions.SectionName))
            .Validate(options => options.MaxHtmlBytes > 0 && options.MaxManualBytes > 0 && options.MaxImageBytes > 0,
                "Upload limits must be positive.")
            .ValidateOnStart();

        services.AddSingleton<IHtmlParser, HtmlParser>();
        services.AddSingleton<IAngularParser, AngularParser>();
        services.AddSingleton<IExistingManualReader, ExistingManualReader>();
        services.AddSingleton<IDocumentationChangeDetector, DocumentationChangeDetector>();
        services.AddSingleton<IPromptBuilder, PromptBuilder>();
        services.AddSingleton<IJobStorageService, JobStorageService>();
        services.AddSingleton<IMarkdownExporter, MarkdownExporter>();
        services.AddSingleton<IWordExporter, WordExporter>();
        services.AddSingleton<IPdfExporter, PdfExporter>();
        services.AddSingleton<IScreenshotAnalyzer, ScreenshotAnalyzer>();
        services.AddScoped<IDocumentationService, DocumentationService>();

        services.AddHttpClient<IOllamaService, OllamaService>((provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<OllamaOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        });
        return services;
    }
}
