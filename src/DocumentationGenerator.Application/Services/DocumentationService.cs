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
        var screenshots = request.Screenshots.ToList();
        if (request.Screenshot is not null) screenshots.Insert(0, request.Screenshot);
        UploadValidator.ValidateScreenshots(screenshots, uploadOptions.Value);

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

        var screenshotResults = new List<ScreenshotAnalysisResult>();
        if (screenshots.Count > 0)
        {
            var visionModel = string.IsNullOrWhiteSpace(request.VisionModel)
                ? ollamaOptions.Value.VisionModel
                : request.VisionModel.Trim();
            if (string.IsNullOrWhiteSpace(visionModel))
            {
                warnings.Add($"{screenshots.Count} screenshot(s) were saved, but no vision model was selected. Visual analysis was skipped.");
            }

            foreach (var screenshot in screenshots)
            {
                var path = await storage.SaveUploadAsync(jobId, screenshot, UploadKind.Screenshot, cancellationToken);
                screen.ScreenshotPaths.Add(path);
                screen.ScreenshotFileNames.Add(screenshot.FileName);
                screen.ScreenshotPath ??= path;
                if (Path.GetExtension(screenshot.FileName).Equals(".webp", StringComparison.OrdinalIgnoreCase))
                    warnings.Add($"{screenshot.FileName} can be visually analyzed, but only PNG and JPEG screenshots are embedded in exported documents.");
                if (string.IsNullOrWhiteSpace(visionModel)) continue;

                try
                {
                    var screenshotResult = await screenshotAnalyzer.AnalyzeAsync(path, visionModel, cancellationToken);
                    screenshotResult.SourceFileName = screenshot.FileName;
                    screenshotResults.Add(screenshotResult);
                    screen.ScreenshotObservations.AddRange(screenshotResult.AllObservations()
                        .Select(observation => $"{screenshot.FileName}: {observation}"));
                    if (!string.IsNullOrWhiteSpace(screenshotResult.Warning)) warnings.Add($"{screenshot.FileName}: {screenshotResult.Warning}");
                    logger.LogInformation("Screenshot {ScreenshotFileName} analysis completed for job {JobId}",
                        screenshot.FileName, jobId);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Screenshot {ScreenshotFileName} analysis failed for job {JobId}",
                        screenshot.FileName, jobId);
                    warnings.Add($"Screenshot analysis failed for {screenshot.FileName}. Other source analysis remains available.");
                }
            }
            screen.ScreenshotObservations = screen.ScreenshotObservations.Distinct().ToList();
        }

        var changes = changeDetector.Detect(screen, existingManual);
        var result = new AnalysisResult
        {
            JobId = jobId,
            Screen = screen,
            ExistingManual = existingManual,
            ScreenshotAnalysis = screenshotResults.FirstOrDefault(),
            ScreenshotAnalyses = screenshotResults,
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
            var screenshotAnalyses = analysis.ScreenshotAnalyses.Count > 0
                ? analysis.ScreenshotAnalyses
                : analysis.ScreenshotAnalysis is null ? [] : [analysis.ScreenshotAnalysis];
            var prompt = promptBuilder.Build(analysis.Screen, analysis.ExistingManual,
                screenshotAnalyses, analysis.Changes);
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
        var screenshotPaths = analysis.Screen.ScreenshotPaths.Count > 0
            ? analysis.Screen.ScreenshotPaths
            : analysis.Screen.ScreenshotPath is null ? [] : [analysis.Screen.ScreenshotPath!];
        manual.ScreenshotPaths = screenshotPaths.Where(IsEmbeddableScreenshot).ToList();
        manual.ScreenshotFileNames = analysis.Screen.ScreenshotFileNames.Count == screenshotPaths.Count
            ? analysis.Screen.ScreenshotFileNames
                .Where((_, index) => IsEmbeddableScreenshot(screenshotPaths[index])).ToList()
            : manual.ScreenshotPaths.Select((path, index) =>
                Path.GetFileName(path) ?? $"Screenshot {index + 1}").ToList();
        manual.CoverImagePath = manual.ScreenshotPaths.FirstOrDefault();
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
            for (var index = 0; index < screenshotPaths.Count; index++)
            {
                var originalName = index < analysis.Screen.ScreenshotFileNames.Count
                    ? analysis.Screen.ScreenshotFileNames[index]
                    : Path.GetFileName(screenshotPaths[index]);
                var analyzed = analysis.ScreenshotAnalyses.Any(result =>
                    result.SourceFileName.Equals(originalName, StringComparison.OrdinalIgnoreCase)) ||
                               analysis.ScreenshotAnalysis?.Succeeded == true && index == 0;
                manual.SourceInformation.Add(new SourceReference
                {
                    SourceType = "Screenshot",
                    FileName = originalName,
                    Summary = analyzed
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

    private static bool IsEmbeddableScreenshot(string path) =>
        Path.GetExtension(path) is ".png" or ".jpg" or ".jpeg";

    private static string Slug(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var chars = value.Select(c => invalid.Contains(c) ? '-' : c).ToArray();
        var slug = string.Join('-', new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrWhiteSpace(slug) ? "user-manual" : slug[..Math.Min(slug.Length, 80)];
    }
}
