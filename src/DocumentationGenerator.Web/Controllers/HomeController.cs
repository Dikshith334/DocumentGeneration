using System.Diagnostics;
using DocumentationGenerator.Application.Configuration;
using DocumentationGenerator.Application.Contracts;
using DocumentationGenerator.Domain.Models;
using DocumentationGenerator.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Options;

namespace DocumentationGenerator.Web.Controllers;

public sealed class HomeController(
    IDocumentationService documentationService,
    IOllamaService ollamaService,
    IJobStorageService storage,
    IOptions<OllamaOptions> ollamaOptions,
    ILogger<HomeController> logger) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var model = new NewGenerationViewModel
        {
            TextModel = ollamaOptions.Value.TextModel,
            VisionModel = ollamaOptions.Value.VisionModel
        };
        await PopulateModelsAsync(model, cancellationToken);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestFormLimits(MultipartBodyLengthLimit = 36_700_160)]
    public async Task<IActionResult> Analyze(NewGenerationViewModel model, CancellationToken cancellationToken)
    {
        if (model.HtmlFile is null) ModelState.AddModelError(nameof(model.HtmlFile), "Choose an HTML file.");
        if (!ModelState.IsValid)
        {
            await PopulateModelsAsync(model, cancellationToken);
            return View("Index", model);
        }

        try
        {
            var result = await documentationService.AnalyzeAsync(new AnalysisRequest
            {
                ScreenName = model.ScreenName,
                HtmlFile = await ReadUploadAsync(model.HtmlFile!, cancellationToken),
                ExistingManual = model.ExistingManual is null ? null : await ReadUploadAsync(model.ExistingManual, cancellationToken),
                Screenshot = model.Screenshot is null ? null : await ReadUploadAsync(model.Screenshot, cancellationToken),
                BusinessRules = model.BusinessRules ?? string.Empty,
                VisionModel = model.VisionModel ?? string.Empty
            }, cancellationToken);
            var page = new AnalysisPageViewModel { Result = result, TextModel = model.TextModel };
            await PopulateModelsAsync(page, cancellationToken);
            return View("Analysis", page);
        }
        catch (ValidationException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            await PopulateModelsAsync(model, cancellationToken);
            return View("Index", model);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Screen analysis failed");
            ModelState.AddModelError(string.Empty, "The screen could not be analyzed. Verify the uploaded files and try again.");
            await PopulateModelsAsync(model, cancellationToken);
            return View("Index", model);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Generate(Guid jobId, string? textModel, CancellationToken cancellationToken)
    {
        try
        {
            var result = await documentationService.GenerateUserManualAsync(new GenerationRequest
            {
                JobId = jobId,
                TextModel = textModel ?? string.Empty
            }, cancellationToken);
            return View("Generated", new GeneratedPageViewModel { Result = result });
        }
        catch (ValidationException exception)
        {
            return View("Generated", new GeneratedPageViewModel
            {
                Result = new GenerationResult { JobId = jobId, ErrorMessage = exception.Message }
            });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Manual generation failed for job {JobId}", jobId);
            return View("Generated", new GeneratedPageViewModel
            {
                Result = new GenerationResult
                {
                    JobId = jobId,
                    ErrorMessage = "Manual generation failed unexpectedly. Review the application logs and try again."
                }
            });
        }
    }

    [HttpGet]
    public async Task<IActionResult> Download(Guid jobId, string fileName, CancellationToken cancellationToken)
    {
        try
        {
            var stream = await storage.OpenDownloadAsync(jobId, fileName, cancellationToken);
            var contentType = Path.GetExtension(fileName).ToLowerInvariant() switch
            {
                ".md" => "text/markdown; charset=utf-8",
                ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                ".pdf" => "application/pdf",
                _ => "application/octet-stream"
            };
            return File(stream, contentType, Path.GetFileName(fileName));
        }
        catch (Exception exception) when (exception is FileNotFoundException or ValidationException)
        {
            return NotFound();
        }
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() => View(new ErrorViewModel
    {
        RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier
    });

    private async Task PopulateModelsAsync(NewGenerationViewModel model, CancellationToken cancellationToken)
    {
        var (available, message, models) = await GetModelsAsync(cancellationToken);
        model.OllamaAvailable = available;
        model.OllamaMessage = message;
        model.Models = ToItems(models, model.TextModel, model.VisionModel);
    }

    private async Task PopulateModelsAsync(AnalysisPageViewModel model, CancellationToken cancellationToken)
    {
        var (_, message, models) = await GetModelsAsync(cancellationToken);
        model.OllamaMessage = message;
        model.Models = ToItems(models, model.TextModel);
    }

    private async Task<(bool Available, string Message, IReadOnlyList<OllamaModel> Models)> GetModelsAsync(
        CancellationToken cancellationToken)
    {
        var health = await ollamaService.CheckHealthAsync(cancellationToken);
        if (!health.IsAvailable) return (false, health.Message, Array.Empty<OllamaModel>());
        try
        {
            var models = await ollamaService.ListModelsAsync(cancellationToken);
            return (true, models.Count == 0 ? "Ollama is running, but no models are installed." : health.Message, models);
        }
        catch (OllamaUnavailableException exception)
        {
            return (false, exception.Message, Array.Empty<OllamaModel>());
        }
    }

    private static List<SelectListItem> ToItems(IReadOnlyList<OllamaModel> models, params string?[] selectedValues)
    {
        var names = models.Select(model => model.Name)
            .Concat(selectedValues.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value)
            .ToList();
        return names.Select(name => new SelectListItem(name, name)).ToList();
    }

    private static async Task<UploadedContent> ReadUploadAsync(IFormFile file, CancellationToken cancellationToken)
    {
        await using var stream = new MemoryStream();
        await file.CopyToAsync(stream, cancellationToken);
        return new UploadedContent { FileName = Path.GetFileName(file.FileName), Content = stream.ToArray() };
    }
}
