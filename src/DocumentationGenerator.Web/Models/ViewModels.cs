using System.ComponentModel.DataAnnotations;
using DocumentationGenerator.Domain.Models;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace DocumentationGenerator.Web.Models;

public sealed class NewGenerationViewModel
{
    [Required, StringLength(120)]
    [Display(Name = "Screen name")]
    public string ScreenName { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Angular or HTML file")]
    public IFormFile? HtmlFile { get; set; }

    [Display(Name = "Existing Word manual (optional)")]
    public IFormFile? ExistingManual { get; set; }

    [Display(Name = "Screen screenshot (optional)")]
    public IFormFile? Screenshot { get; set; }

    [Display(Name = "Business rules (optional)")]
    public string? BusinessRules { get; set; }

    [Display(Name = "Text model")]
    public string? TextModel { get; set; }

    [Display(Name = "Vision model")]
    public string? VisionModel { get; set; }

    public List<SelectListItem> Models { get; set; } = [];
    public bool OllamaAvailable { get; set; }
    public string OllamaMessage { get; set; } = string.Empty;
}

public sealed class AnalysisPageViewModel
{
    public required AnalysisResult Result { get; init; }
    public string? TextModel { get; set; }
    public List<SelectListItem> Models { get; set; } = [];
    public string OllamaMessage { get; set; } = string.Empty;
}

public sealed class GeneratedPageViewModel
{
    public required GenerationResult Result { get; init; }
}

public sealed class ErrorViewModel
{
    public string? RequestId { get; set; }
    public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);
}
