namespace DocumentationGenerator.Application.Configuration;

public sealed class OllamaOptions
{
    public const string SectionName = "Ollama";
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string TextModel { get; set; } = string.Empty;
    public string VisionModel { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 300;
    public double Temperature { get; set; } = 0.2;
}

public sealed class StorageOptions
{
    public const string SectionName = "Storage";
    public string RootPath { get; set; } = "App_Data/Jobs";
    public int CleanupAfterHours { get; set; } = 72;
}

public sealed class UploadOptions
{
    public const string SectionName = "Uploads";
    public long MaxHtmlBytes { get; set; } = 2 * 1024 * 1024;
    public long MaxManualBytes { get; set; } = 20 * 1024 * 1024;
    public long MaxImageBytes { get; set; } = 10 * 1024 * 1024;
    public int MaxScreenshotCount { get; set; } = 10;
    public long MaxScreenshotTotalBytes { get; set; } = 50 * 1024 * 1024;
}
