using DocumentationGenerator.Domain.Models;

namespace DocumentationGenerator.Application.Contracts;

public interface IHtmlParser
{
    Task<Screen> ParseAsync(string html, string screenName, string sourceFileName,
        CancellationToken cancellationToken = default);
}

public interface IAngularParser : IHtmlParser;

public interface IExistingManualReader
{
    Task<ExistingManual> ReadAsync(string path, string originalFileName,
        CancellationToken cancellationToken = default);
}

public interface IScreenshotAnalyzer
{
    Task<ScreenshotAnalysisResult> AnalyzeAsync(string path, string model,
        CancellationToken cancellationToken = default);
}

public interface IDocumentationChangeDetector
{
    DocumentationChangeSet Detect(Screen screen, ExistingManual? manual);
}

public interface IPromptBuilder
{
    string Build(Screen screen, ExistingManual? manual, IReadOnlyCollection<ScreenshotAnalysisResult> screenshots,
        DocumentationChangeSet changes);
}

public interface IOllamaService
{
    Task<OllamaHealthResult> CheckHealthAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OllamaModel>> ListModelsAsync(CancellationToken cancellationToken = default);
    Task<string> GenerateTextAsync(string model, string prompt,
        CancellationToken cancellationToken = default);
    Task<string> GenerateVisionTextAsync(string model, string prompt, string base64Image,
        CancellationToken cancellationToken = default);
    Task<T> GenerateJsonAsync<T>(string model, string prompt,
        CancellationToken cancellationToken = default) where T : class;
}

public interface IDocumentationService
{
    Task<AnalysisResult> AnalyzeAsync(AnalysisRequest request,
        CancellationToken cancellationToken = default);
    Task<GenerationResult> GenerateUserManualAsync(GenerationRequest request,
        CancellationToken cancellationToken = default);
}

public interface IMarkdownExporter
{
    Task ExportAsync(UserManual manual, string outputPath,
        CancellationToken cancellationToken = default);
}

public interface IWordExporter
{
    Task ExportAsync(UserManual manual, string outputPath,
        CancellationToken cancellationToken = default);
}

public interface IPdfExporter
{
    Task ExportAsync(UserManual manual, string outputPath,
        CancellationToken cancellationToken = default);
}

public enum UploadKind
{
    Html,
    ExistingManual,
    Screenshot
}

public interface IJobStorageService
{
    Task<Guid> CreateJobAsync(CancellationToken cancellationToken = default);
    Task<string> SaveUploadAsync(Guid jobId, UploadedContent file, UploadKind kind,
        CancellationToken cancellationToken = default);
    Task SaveJsonAsync<T>(Guid jobId, string fileName, T value,
        CancellationToken cancellationToken = default);
    Task<T?> LoadJsonAsync<T>(Guid jobId, string fileName,
        CancellationToken cancellationToken = default);
    string GetOutputPath(Guid jobId, string fileName);
    Task<Stream> OpenDownloadAsync(Guid jobId, string fileName,
        CancellationToken cancellationToken = default);
    Task CleanupExpiredJobsAsync(CancellationToken cancellationToken = default);
}
