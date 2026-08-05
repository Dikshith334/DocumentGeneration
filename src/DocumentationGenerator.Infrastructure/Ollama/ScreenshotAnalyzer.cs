using System.Text.Json;
using DocumentationGenerator.Application.Contracts;
using DocumentationGenerator.Domain.Models;

namespace DocumentationGenerator.Infrastructure.Ollama;

public sealed class ScreenshotAnalyzer(IOllamaService ollamaService) : IScreenshotAnalyzer
{
    public async Task<ScreenshotAnalysisResult> AnalyzeAsync(string path, string model,
        CancellationToken cancellationToken = default)
    {
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        var base64 = Convert.ToBase64String(bytes);
        const string prompt = """
            Analyze this application screenshot as visible evidence for a user manual.
            Do not invent behavior, permissions, navigation, validation, or hidden controls.
            Distinguish visible text from inferred purpose; include only visible UI facts.
            Return strict JSON only using this shape:
            {
              "succeeded": true,
              "screenTitle": "",
              "mainSections": [],
              "buttons": [],
              "forms": [],
              "tables": [],
              "charts": [],
              "tabs": [],
              "filters": [],
              "icons": [],
              "navigationElements": [],
              "layoutObservations": [],
              "messages": [],
              "warning": null
            }
            """;
        var response = await ollamaService.GenerateVisionTextAsync(model, prompt, base64, cancellationToken);
        try
        {
            var result = OllamaJsonParser.Deserialize<ScreenshotAnalysisResult>(response);
            result.Succeeded = true;
            return result;
        }
        catch (JsonException ex)
        {
            throw new OllamaResponseException($"The vision model returned invalid JSON: {ex.Message}");
        }
    }
}
