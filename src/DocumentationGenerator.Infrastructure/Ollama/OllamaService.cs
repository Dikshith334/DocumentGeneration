using System.Net.Http.Json;
using System.Text.Json;
using DocumentationGenerator.Application.Configuration;
using DocumentationGenerator.Application.Contracts;
using DocumentationGenerator.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocumentationGenerator.Infrastructure.Ollama;

public sealed class OllamaService(
    HttpClient httpClient,
    IOptions<OllamaOptions> options,
    ILogger<OllamaService> logger) : IOllamaService
{
    private readonly OllamaOptions _options = options.Value;

    public async Task<OllamaHealthResult> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await httpClient.GetAsync("api/version", cancellationToken);
            return response.IsSuccessStatusCode
                ? new OllamaHealthResult(true, "Ollama is available.")
                : new OllamaHealthResult(false, ActionableUnavailableMessage());
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return new OllamaHealthResult(false, ActionableUnavailableMessage());
        }
    }

    public async Task<IReadOnlyList<OllamaModel>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await httpClient.GetAsync("api/tags", cancellationToken);
            if (!response.IsSuccessStatusCode) throw new OllamaUnavailableException(ActionableUnavailableMessage());
            var payload = await response.Content.ReadFromJsonAsync<ModelListResponse>(cancellationToken: cancellationToken);
            return payload?.Models.Select(model => new OllamaModel(model.Name, model.Size, model.ModifiedAt)).ToList() ?? [];
        }
        catch (OllamaUnavailableException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException &&
                                   !cancellationToken.IsCancellationRequested)
        {
            throw new OllamaUnavailableException(ActionableUnavailableMessage(), ex);
        }
    }

    public async Task<string> GenerateTextAsync(string model, string prompt,
        CancellationToken cancellationToken = default)
    {
        await EnsureModelInstalledAsync(model, cancellationToken);
        return await PostChatAsync(model, prompt, null, cancellationToken);
    }

    public async Task<string> GenerateVisionTextAsync(string model, string prompt, string base64Image,
        CancellationToken cancellationToken = default)
    {
        await EnsureModelInstalledAsync(model, cancellationToken);
        return await PostChatAsync(model, prompt, [base64Image], cancellationToken);
    }

    public async Task<T> GenerateJsonAsync<T>(string model, string prompt,
        CancellationToken cancellationToken = default) where T : class
    {
        var firstResponse = await GenerateTextAsync(model, prompt, cancellationToken);
        try
        {
            return OllamaJsonParser.Deserialize<T>(firstResponse);
        }
        catch (JsonException)
        {
            logger.LogWarning("Ollama returned invalid JSON; requesting one repair attempt");
        }

        var repairPrompt = "Return corrected strict JSON only for the following invalid response. " +
                           "Do not add Markdown fences or commentary. Preserve the intended information:\n" + firstResponse;
        var repaired = await PostChatAsync(model, repairPrompt, null, cancellationToken);
        try
        {
            return OllamaJsonParser.Deserialize<T>(repaired);
        }
        catch (JsonException ex)
        {
            throw new OllamaResponseException(
                $"Ollama returned invalid JSON after one repair attempt. Response preview: {OllamaJsonParser.SanitizePreview(repaired)}. {ex.Message}");
        }
    }

    private async Task EnsureModelInstalledAsync(string model, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(model)) throw new ValidationException("An Ollama model must be selected.");
        var models = await ListModelsAsync(cancellationToken);
        var installed = models.Any(candidate =>
            candidate.Name.Equals(model, StringComparison.OrdinalIgnoreCase) ||
            (!model.Contains(':') && candidate.Name.StartsWith(model + ":", StringComparison.OrdinalIgnoreCase)));
        if (!installed)
        {
            throw new OllamaUnavailableException(
                $"The Ollama model '{model}' is not installed. Run 'ollama pull {model}' and try again.");
        }
    }

    private async Task<string> PostChatAsync(string model, string prompt, IReadOnlyList<string>? images,
        CancellationToken cancellationToken)
    {
        var message = new Dictionary<string, object?>
        {
            ["role"] = "user",
            ["content"] = prompt
        };
        if (images is { Count: > 0 }) message["images"] = images;
        var request = new
        {
            model,
            messages = new[] { message },
            stream = false,
            format = "json",
            options = new { temperature = _options.Temperature }
        };

        try
        {
            logger.LogInformation("Sending local Ollama request using model {Model}", model);
            using var response = await httpClient.PostAsJsonAsync("api/chat", request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var detail = OllamaJsonParser.SanitizePreview(responseBody);
                throw new OllamaUnavailableException(
                    $"Ollama rejected the request for model '{model}' ({(int)response.StatusCode}). {detail}");
            }
            using var json = JsonDocument.Parse(responseBody);
            if (!json.RootElement.TryGetProperty("message", out var messageElement) ||
                !messageElement.TryGetProperty("content", out var contentElement))
            {
                throw new OllamaResponseException("Ollama returned a response without message content.");
            }
            return contentElement.GetString() ?? string.Empty;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException &&
                                   !cancellationToken.IsCancellationRequested)
        {
            throw new OllamaUnavailableException(ActionableUnavailableMessage(), ex);
        }
        catch (JsonException ex)
        {
            throw new OllamaResponseException($"Ollama returned an unreadable HTTP response: {ex.Message}");
        }
    }

    private string ActionableUnavailableMessage() =>
        $"Ollama could not be reached at {_options.BaseUrl.TrimEnd('/')}. Start Ollama and verify that the selected model is installed.";

    private sealed class ModelListResponse
    {
        public List<ModelResponse> Models { get; set; } = [];
    }

    private sealed class ModelResponse
    {
        public string Name { get; set; } = string.Empty;
        public long Size { get; set; }
        public DateTimeOffset? ModifiedAt { get; set; }
    }
}
