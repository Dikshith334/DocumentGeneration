using System.Text.Json;
using DocumentationGenerator.Application.Configuration;
using DocumentationGenerator.Application.Contracts;
using DocumentationGenerator.Domain.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace DocumentationGenerator.Infrastructure.Storage;

public sealed class JobStorageService : IJobStorageService
{
    private readonly string _rootPath;
    private readonly StorageOptions _options;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public JobStorageService(IOptions<StorageOptions> options, IHostEnvironment environment)
    {
        _options = options.Value;
        _rootPath = Path.GetFullPath(Path.IsPathRooted(_options.RootPath)
            ? _options.RootPath
            : Path.Combine(environment.ContentRootPath, _options.RootPath));
    }

    public Task<Guid> CreateJobAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var id = Guid.NewGuid();
        Directory.CreateDirectory(GetJobPath(id));
        Directory.CreateDirectory(Path.Combine(GetJobPath(id), "uploads"));
        Directory.CreateDirectory(Path.Combine(GetJobPath(id), "output"));
        return Task.FromResult(id);
    }

    public async Task<string> SaveUploadAsync(Guid jobId, UploadedContent file, UploadKind kind,
        CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var generatedName = kind switch
        {
            UploadKind.Html => "screen" + extension,
            UploadKind.ExistingManual => "existing-manual.docx",
            UploadKind.Screenshot => $"screenshot-{Guid.NewGuid():N}" + extension,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        var path = SafeCombine(Path.Combine(GetJobPath(jobId), "uploads"), generatedName);
        await File.WriteAllBytesAsync(path, file.Content, cancellationToken);
        return path;
    }

    public async Task SaveJsonAsync<T>(Guid jobId, string fileName, T value,
        CancellationToken cancellationToken = default)
    {
        var path = SafeCombine(GetJobPath(jobId), fileName);
        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None,
            81920, FileOptions.Asynchronous);
        await JsonSerializer.SerializeAsync(stream, value, _jsonOptions, cancellationToken);
    }

    public async Task<T?> LoadJsonAsync<T>(Guid jobId, string fileName,
        CancellationToken cancellationToken = default)
    {
        var path = SafeCombine(GetJobPath(jobId), fileName);
        if (!File.Exists(path)) return default;
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, FileOptions.Asynchronous);
        return await JsonSerializer.DeserializeAsync<T>(stream, _jsonOptions, cancellationToken);
    }

    public string GetOutputPath(Guid jobId, string fileName)
    {
        var outputDirectory = Path.Combine(GetJobPath(jobId), "output");
        Directory.CreateDirectory(outputDirectory);
        return SafeCombine(outputDirectory, fileName);
    }

    public Task<Stream> OpenDownloadAsync(Guid jobId, string fileName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var extension = Path.GetExtension(fileName);
        if (extension is not (".md" or ".docx" or ".pdf"))
        {
            throw new ValidationException("Unsupported download type.");
        }
        var path = SafeCombine(Path.Combine(GetJobPath(jobId), "output"), fileName);
        if (!File.Exists(path)) throw new FileNotFoundException("The generated file was not found.");
        Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Task.FromResult(stream);
    }

    public Task CleanupExpiredJobsAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_rootPath)) return Task.CompletedTask;
        var cutoff = DateTime.UtcNow.AddHours(-Math.Max(1, _options.CleanupAfterHours));
        foreach (var directory in Directory.EnumerateDirectories(_rootPath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Directory.GetCreationTimeUtc(directory) < cutoff) Directory.Delete(directory, true);
        }
        return Task.CompletedTask;
    }

    private string GetJobPath(Guid jobId)
    {
        if (jobId == Guid.Empty) throw new ValidationException("Invalid job identifier.");
        return SafeCombine(_rootPath, jobId.ToString("N"));
    }

    private static string SafeCombine(string parent, string child)
    {
        if (string.IsNullOrWhiteSpace(child) || Path.GetFileName(child) != child || child.Contains("..", StringComparison.Ordinal))
        {
            throw new ValidationException("Invalid file name.");
        }
        var fullParent = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(Path.Combine(fullParent, child));
        if (!candidate.StartsWith(fullParent, StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException("The requested path is outside the job directory.");
        }
        return candidate;
    }
}
