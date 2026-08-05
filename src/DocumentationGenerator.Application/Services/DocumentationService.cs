using System.Text;
using DocumentationGenerator.Application.Configuration;
using DocumentationGenerator.Application.Contracts;
using DocumentationGenerator.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocumentationGenerator.Application.Services;

/// <summary>Coordinates secure upload, source analysis, AI generation, and all document exports.</summary>
public sealed class DocumentationService(
    IHtmlParser htmlParser,
    IAngularParser angularParser,
    IExistingManualReader manualReader,
    IScreenshotAnalyzer screenshotAnalyzer,
    IDocumentationChangeDetector changeDetector,
    IPromptBuilder promptBuilder,
    IOllamaService ollamaService,
    IMarkdownExporter markdownExporter,
    IWordExporter wordExporter,
    IPdfExporter pdfExporter,
    IJobStorageService storage,
    IOptions<UploadOptions> uploadOptions,
    IOptions<OllamaOptions> ollamaOptions,
    ILogger<DocumentationService> logger) : IDocumentationService
{
    public async Task<AnalysisResult> AnalyzeAsync(AnalysisRequest request,
        CancellationToken cancellationToken = default)
    {
        UploadValidator.Validate(request.HtmlFile, UploadKind.Html, uploadOptions.Value);
        if (request.ExistingManual is not null)
        {
            UploadValidator.Validate(request.ExistingManual, UploadKind.ExistingManual, uploadOptions.Value);
        }
        if (request.Screenshot is not null)
        {
            UploadValidator.Validate(request.Screenshot, UploadKind.Screenshot, uploadOptions.Value);
        }

        var jobId = await storage.CreateJobAsync(cancellationToken);
        logger.LogInformation("Job {JobId} started", jobId);
        var htmlPath = await storage.SaveUploadAsync(jobId, request.HtmlFile, UploadKind.Html, cancellationToken);
        var html = DecodeHtml(request.HtmlFile.Content);
        var name = string.IsNullOrWhiteSpace(request.ScreenName)
            ? Path.GetFileNameWithoutExtension(request.HtmlFile.FileName)
            : request.ScreenName.Trim();

        var isAngular = LooksAngular(html);
        var screen = await (isAngular ? angularParser : htmlParser)
            .ParseAsync(html, name, request.HtmlFile.FileName, cancellationToken);
        screen.BusinessRules = request.BusinessRules.Trim();
        logger.LogInformation("Parsed job {JobId}: {ElementCount} UI elements", jobId, screen.ElementCount);

        var warnings = new List<string>();
        ExistingManual? existingManual = null;
        if (request.ExistingManual is not null)
        {
            var path = await storage.SaveUploadAsync(jobId, request.ExistingManual, UploadKind.ExistingManual, cancellationToken);
            screen.ExistingManualPath = path;
            try
            {
                existingManual = await manualReader.ReadAsync(path, request.ExistingManual.FileName, cancellationToken);
                logger.LogInformation("Existing manual read for job {JobId}", jobId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Existing manual could not be read for job {JobId}", jobId);
                warnings.Add("The existing Word manual could not be read. Analysis continued without it.");
            }
        }

        ScreenshotAnalysisResult? screenshotResult = null;
        if (request.Screenshot is not null)
        {
            var path = await storage.SaveUploadAsync(jobId, request.Screenshot, UploadKind.Screenshot, cancellationToken);
            screen.ScreenshotPath = path;
            var visionModel = string.IsNullOrWhiteSpace(request.VisionModel)
                ? ollamaOptions.Value.VisionModel
                : request.VisionModel.Trim();
            if (string.IsNullOrWhiteSpace(visionModel))
            {
                warnings.Add("A screenshot was saved, but no vision model was selected. Screenshot analysis was skipped.");
            }
            else
            {
                try
                {
                    screenshotResult = await screenshotAnalyzer.AnalyzeAsync(path, visionModel, cancellationToken);
                    screen.ScreenshotObservations = screenshotResult.AllObservations().Distinct().ToList();
                    if (!string.IsNullOrWhiteSpace(screenshotResult.Warning))
                    {
                        warnings.Add(screenshotResult.Warning);
                    }
                    logger.LogInformation("Screenshot analysis completed for job {JobId}", jobId);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Screenshot analysis failed for job {JobId}", jobId);
                    warnings.Add("Screenshot analysis failed. HTML, existing manual, and business-rule analysis remains available.");
                }
            }
        }

        var changes = changeDetector.Detect(screen, existingManual);
        var result = new AnalysisResult
        {
            JobId = jobId,
            Screen = screen,
            ExistingManual = existingManual,
            ScreenshotAnalysis = screenshotResult,
            Changes = changes,
            Warnings = warnings
        };
        await storage.SaveJsonAsync(jobId, "analysis.json", new JobSnapshot { Analysis = result }, cancellationToken);
        return result;
    }

    public async Task<GenerationResult> GenerateUserManualAsync(GenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await storage.LoadJsonAsync<JobSnapshot>(request.JobId, "analysis.json", cancellationToken)
            ?? throw new ValidationException("The analysis job was not found or has expired.");
        var model = string.IsNullOrWhiteSpace(request.TextModel) ? ollamaOptions.Value.TextModel : request.TextModel.Trim();
        if (string.IsNullOrWhiteSpace(model))
        {
            return Failure(request.JobId,
                "Select an installed Ollama text model before generating the manual.", snapshot.Analysis.Warnings);
        }

        try
        {
            var analysis = snapshot.Analysis;
            var prompt = promptBuilder.Build(analysis.Screen, analysis.ExistingManual,
                analysis.ScreenshotAnalysis, analysis.Changes);
            logger.LogInformation("Sending Ollama generation request for job {JobId}", request.JobId);
            var manual = await ollamaService.GenerateJsonAsync<UserManual>(model, prompt, cancellationToken);
            NormalizeManual(manual, analysis);

            var safeBaseName = Slug(manual.Title);
            var markdownName = $"{safeBaseName}.md";
            var wordName = $"{safeBaseName}.docx";
            var pdfName = $"{safeBaseName}.pdf";
            await markdownExporter.ExportAsync(manual, storage.GetOutputPath(request.JobId, markdownName), cancellationToken);
            await wordExporter.ExportAsync(manual, storage.GetOutputPath(request.JobId, wordName), cancellationToken);
            await pdfExporter.ExportAsync(manual, storage.GetOutputPath(request.JobId, pdfName), cancellationToken);
            await storage.SaveJsonAsync(request.JobId, "manual.json", manual, cancellationToken);
            logger.LogInformation("All exports completed for job {JobId}", request.JobId);

            return new GenerationResult
            {
                JobId = request.JobId,
                Succeeded = true,
                Manual = manual,
                MarkdownFileName = markdownName,
                WordFileName = wordName,
                PdfFileName = pdfName,
                Warnings = analysis.Warnings
            };
        }
        catch (OllamaUnavailableException ex)
        {
            logger.LogWarning("Ollama unavailable for job {JobId}: {Message}", request.JobId, ex.Message);
            return Failure(request.JobId, ex.Message, snapshot.Analysis.Warnings);
        }
        catch (OllamaResponseException ex)
        {
            logger.LogWarning("Ollama returned invalid output for job {JobId}: {Message}", request.JobId, ex.Message);
            return Failure(request.JobId, ex.Message, snapshot.Analysis.Warnings);
        }
    }

    private static GenerationResult Failure(Guid jobId, string message, IEnumerable<string> warnings) => new()
    {
        JobId = jobId,
        Succeeded = false,
        ErrorMessage = message,
        Warnings = warnings.ToList()
    };

    private static void NormalizeManual(UserManual manual, AnalysisResult analysis)
    {
        ManualCompleter.Complete(manual, analysis);
        if (string.IsNullOrWhiteSpace(manual.Title))
        {
            manual.Title = $"{analysis.Screen.Name} User Manual";
        }
        manual.GeneratedDate = DateTimeOffset.UtcNow;
        manual.CoverImagePath = analysis.Screen.ScreenshotPath;
        if (manual.SourceInformation.Count == 0)
        {
            manual.SourceInformation.Add(new SourceReference
            {
                SourceType = "HTML",
                FileName = analysis.Screen.SourceFileName,
                Summary = $"Parsed {analysis.Screen.ElementCount} UI elements."
            });
            if (analysis.ExistingManual is not null)
            {
                manual.SourceInformation.Add(new SourceReference
                {
                    SourceType = "Existing Word manual",
                    FileName = analysis.ExistingManual.FileName,
                    Summary = "Used for accurate existing content and writing-style guidance."
                });
            }
            if (analysis.Screen.ScreenshotPath is not null)
            {
                manual.SourceInformation.Add(new SourceReference
                {
                    SourceType = "Screenshot",
                    FileName = Path.GetFileName(analysis.Screen.ScreenshotPath),
                    Summary = analysis.ScreenshotAnalysis?.Succeeded == true
                        ? "Used for visible layout observations."
                        : "Supplied but not analyzed."
                });
            }
        }
    }

    private static string DecodeHtml(byte[] content)
    {
        using var reader = new StreamReader(new MemoryStream(content), Encoding.UTF8, true);
        return reader.ReadToEnd();
    }

    private static bool LooksAngular(string html) =>
        html.Contains("(click)", StringComparison.OrdinalIgnoreCase) ||
        html.Contains("*ngIf", StringComparison.OrdinalIgnoreCase) ||
        html.Contains("[(ngModel)]", StringComparison.OrdinalIgnoreCase) ||
        html.Contains("formControlName", StringComparison.OrdinalIgnoreCase) ||
        html.Contains("<mat-", StringComparison.OrdinalIgnoreCase);

    private static string Slug(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var chars = value.Select(c => invalid.Contains(c) ? '-' : c).ToArray();
        var slug = string.Join('-', new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrWhiteSpace(slug) ? "user-manual" : slug[..Math.Min(slug.Length, 80)];
    }
}
