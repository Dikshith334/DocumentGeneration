using DocumentationGenerator.Application.Configuration;
using DocumentationGenerator.Application.Contracts;
using DocumentationGenerator.Domain.Models;

namespace DocumentationGenerator.Application.Services;

public static class UploadValidator
{
    private static readonly HashSet<string> HtmlExtensions = new(StringComparer.OrdinalIgnoreCase) { ".html", ".htm" };
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".webp" };

    public static void Validate(UploadedContent file, UploadKind kind, UploadOptions options)
    {
        if (file.Content.Length == 0)
        {
            throw new ValidationException($"{FriendlyName(kind)} cannot be empty.");
        }

        var extension = Path.GetExtension(Path.GetFileName(file.FileName));
        var validExtension = kind switch
        {
            UploadKind.Html => HtmlExtensions.Contains(extension),
            UploadKind.ExistingManual => extension.Equals(".docx", StringComparison.OrdinalIgnoreCase),
            UploadKind.Screenshot => ImageExtensions.Contains(extension),
            _ => false
        };

        if (!validExtension)
        {
            throw new ValidationException($"{FriendlyName(kind)} has an unsupported file extension.");
        }

        var limit = kind switch
        {
            UploadKind.Html => options.MaxHtmlBytes,
            UploadKind.ExistingManual => options.MaxManualBytes,
            UploadKind.Screenshot => options.MaxImageBytes,
            _ => 0
        };

        if (file.Content.LongLength > limit)
        {
            throw new ValidationException($"{FriendlyName(kind)} exceeds the configured upload limit.");
        }

        if (Path.GetFileName(file.FileName) != file.FileName || file.FileName.Contains("..", StringComparison.Ordinal))
        {
            throw new ValidationException("Uploaded file names must not contain path traversal sequences.");
        }

        ValidateSignature(file, kind);
    }

    private static void ValidateSignature(UploadedContent file, UploadKind kind)
    {
        var bytes = file.Content;
        var valid = kind switch
        {
            UploadKind.ExistingManual => bytes.Length >= 4 && bytes[0] == 0x50 && bytes[1] == 0x4B,
            UploadKind.Screenshot when Path.GetExtension(file.FileName).Equals(".png", StringComparison.OrdinalIgnoreCase) =>
                bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47,
            UploadKind.Screenshot when Path.GetExtension(file.FileName).Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                                           Path.GetExtension(file.FileName).Equals(".jpeg", StringComparison.OrdinalIgnoreCase) =>
                bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF,
            UploadKind.Screenshot when Path.GetExtension(file.FileName).Equals(".webp", StringComparison.OrdinalIgnoreCase) =>
                bytes.Length >= 12 && bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) && bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8),
            _ => true
        };

        if (!valid)
        {
            throw new ValidationException($"{FriendlyName(kind)} content does not match its file extension.");
        }
    }

    private static string FriendlyName(UploadKind kind) => kind switch
    {
        UploadKind.Html => "HTML file",
        UploadKind.ExistingManual => "Existing manual",
        UploadKind.Screenshot => "Screenshot",
        _ => "Upload"
    };
}
