# AI-Powered User Manual Generator

A complete local-first ASP.NET Core MVC application that analyzes Angular or ordinary
HTML screens, compares an optional existing Word manual, optionally examines a screenshot
with an Ollama vision model, and generates a structured professional user manual through a
local Ollama text model. Generated manuals can be previewed and downloaded as Markdown,
Word, or PDF.

The application does not require a database, authentication, Microsoft Word, or a cloud
service. Uploaded content is stored only in an application-controlled job directory and is
sent only to the configured local Ollama endpoint.

## Architecture

```text
DocumentationGenerator.Domain
  Domain models with no solution-project dependencies
           ↑
DocumentationGenerator.Application
  Contracts, options, validation, prompt construction, workflow orchestration
           ↑
DocumentationGenerator.Infrastructure
  AngleSharp, Open XML, PDFsharp, storage, change detection, Ollama HTTP client
           ↑
DocumentationGenerator.Web
  ASP.NET Core MVC upload, review, preview, and safe download workflow

DocumentationGenerator.Tests
  Parser, DOCX, change detection, prompting, Ollama, export, and security tests
```

The solution uses dependency injection throughout. `DocumentationService` is the workflow
orchestrator; infrastructure details are accessed only through application interfaces.
No uploaded content, complete prompt, screenshot, business-rule text, or manual body is
written to application logs.

## Solution layout

```text
DocumentationGenerator.sln
src/
  DocumentationGenerator.Domain/
  DocumentationGenerator.Application/
  DocumentationGenerator.Infrastructure/
  DocumentationGenerator.Web/
tests/
  DocumentationGenerator.Tests/
samples/
  CustomerManagement/
    customer-management.component.html
    business-rules.txt
    README.md
```

## Prerequisites

- .NET 10 LTS SDK (`10.0.302` or a compatible patch). The project targets `net10.0`.
- Ollama installed locally for screenshot analysis and AI manual generation.
- At least one Ollama text model for generation.
- A vision-capable Ollama model only when screenshot analysis is required.

The Analyze stage works without Ollama. The Generate stage intentionally returns an
actionable message until a reachable Ollama instance and installed text model are available.

## Set up Ollama

Start the local service:

```bash
ollama serve
```

In another terminal, list installed models:

```bash
ollama list
```

Pull a text model supported by your Ollama installation:

```bash
ollama pull <text-model-name>
```

Pull a vision-capable model when screenshot analysis is needed:

```bash
ollama pull <vision-model-name>
```

No model name is hardcoded. When Ollama is reachable, the web page reads `/api/tags` and
offers the locally installed models. Configure defaults only if desired.

## Configuration

Settings live in `src/DocumentationGenerator.Web/appsettings.json`:

```json
{
  "Ollama": {
    "BaseUrl": "http://localhost:11434",
    "TextModel": "",
    "VisionModel": "",
    "TimeoutSeconds": 300,
    "Temperature": 0.2
  },
  "Storage": {
    "RootPath": "App_Data/Jobs",
    "CleanupAfterHours": 72
  },
  "Uploads": {
    "MaxHtmlBytes": 2097152,
    "MaxManualBytes": 20971520,
    "MaxImageBytes": 10485760,
    "MaxRequestBytes": 36700160
  }
}
```

ASP.NET Core environment variables use double underscores:

```powershell
$env:Ollama__BaseUrl = "http://localhost:11434"
$env:Ollama__TextModel = "your-installed-text-model"
$env:Ollama__VisionModel = "your-installed-vision-model"
```

Equivalent examples are included in `.env.example`. The application never commits a
machine-specific storage path. Relative storage paths are resolved under the web project's
content root. Antiforgery data-protection keys are also kept under `App_Data/Keys`, keeping
the local application self-contained.

## Build, test, and run

From the repository root:

```bash
dotnet restore
dotnet build
dotnet test
dotnet run --project src/DocumentationGenerator.Web
```

Open the HTTP or HTTPS address printed in the terminal. The development launch profile is
configured in `src/DocumentationGenerator.Web/Properties/launchSettings.json`.

## Sample workflow

1. Open **Manual Studio**.
2. Enter `Customer Management` as the screen name.
3. Upload `samples/CustomerManagement/customer-management.component.html`.
4. Paste `samples/CustomerManagement/business-rules.txt` into **Business rules**.
5. Optionally upload a `.docx` existing manual.
6. Optionally upload a PNG, JPEG, or WebP screenshot and choose an installed vision model.
7. Select **Analyze screen**.
8. Review buttons, fields, dropdowns, filters, table columns, screenshot observations, and
   documentation changes.
9. Choose an installed text model and select **Generate user manual**.
10. Preview the result and download Markdown, Word, or PDF.

The included sample contains Import, Export, Add, Search, Advanced Filter, Edit, and Delete
actions; Angular click handlers; an Admin-only Delete condition; search and status filters;
six customer table columns; sorting; tooltips; and pagination.

## Processing flow

```text
Angular or HTML + optional DOCX + optional screenshot + business rules
                              ↓
            safe upload and static source parsing
                              ↓
       Word reading + optional local vision analysis
                              ↓
       normalized documentation change detection
                              ↓
   source-prioritized, anti-hallucination prompt construction
                              ↓
             local Ollama structured JSON generation
                              ↓
      validated UserManual → Markdown + Word + PDF
```

Business rules have the highest source priority. HTML attributes and Angular bindings
override screenshot guesses. The prompt explicitly prohibits invented behavior,
permissions, navigation, validation, examples, and outcomes. Unsupported details must be
reported as not specified or requiring review.

## Implementation details

- **HTML and Angular parsing:** AngleSharp extracts ordinary and Angular Material controls,
  bindings, handlers, visibility/disabled conditions, labels, tabs, filters, tables,
  sortable columns, empty states, breadcrumbs, and pagination. Uploaded HTML is parsed as
  inert text and never rendered or executed.
- **Existing manuals:** Open XML reads paragraphs, headings, styles, document-order tables,
  clean plain text, numbered-heading usage, description detail, and table-based feature
  documentation.
- **Change detection:** normalized phrase matching and Levenshtein similarity distinguish
  added, existing, possibly renamed, and possibly removed items. Uncertain removals always
  require review because static HTML cannot prove a runtime removal.
- **Screenshot analysis:** images are Base64-encoded and sent to the selected local vision
  model with a visible-evidence-only JSON prompt. Failure is a warning and does not block
  HTML/manual analysis.
- **Ollama:** `IHttpClientFactory` is used for `/api/version`, `/api/tags`, and `/api/chat`,
  with timeout, cancellation, installed-model validation, low temperature, JSON cleanup,
  and one repair request for malformed JSON.
- **Documents:** the Word and PDF layouts follow the supplied reference manual's hierarchy:
  centered cover, screen overview, numbered feature index, numbered task sections,
  structured tables, and page-number footers. PDFsharp uses a cross-platform resolver for
  common Windows, Linux, and macOS sans-serif fonts.
- **Storage:** each job uses `App_Data/Jobs/{jobId}/uploads` and `output`; generated names are
  used for uploads, safe-combine checks prevent traversal, and expired jobs are removed on
  startup according to the cleanup policy.

## Security and validation

- Source files: `.html` and `.htm` only.
- Existing manuals: `.docx` only, with ZIP signature validation.
- Screenshots: `.png`, `.jpg`, `.jpeg`, and `.webp`, with basic signature validation.
- Upload limits are configurable per file type and for the total multipart request.
- Original upload paths are never trusted; only base names and generated storage names are
  used.
- Preview values are rendered by Razor's default HTML encoding.
- Downloads are restricted to the requested job's output directory and `.md`, `.docx`, or
  `.pdf` files.
- No full local filesystem path is rendered in the browser.
- No remote endpoint other than configured Ollama is called by application code.

## Automated tests

The xUnit suite does not require Ollama. HTTP responses are supplied by a queued mock
handler. Current coverage includes:

- Angular buttons, click handlers, `*ngIf`, labels, fields, bindings, filters, table headers,
  sorting, and pagination.
- Existing Word paragraphs, headings, tables, and style observations.
- Added/existing/possibly removed documentation items.
- Prompt anti-hallucination rules and business-rule priority.
- Ollama fence/surrounding-text cleanup, successful JSON repair, and invalid repair failure.
- Markdown content, valid professional Word output, and valid paginated PDF output.
- File-extension, file-signature, upload-size, and path-traversal validation.

Run:

```bash
dotnet test DocumentationGenerator.sln
```

## Troubleshooting

### Ollama could not be reached

Start Ollama, confirm `Ollama:BaseUrl`, and verify the endpoint from the same machine. The UI
shows the exact configured URL but does not expose uploaded content.

### Selected model is not installed

Run `ollama list`, then either choose an installed name in the UI or pull the configured
model with `ollama pull <model-name>`.

### Screenshot analysis failed

Confirm that the selected model supports images and that the screenshot is within the
configured limit. Continue with HTML/manual analysis if visual analysis is unnecessary.

### An uploaded file is rejected

Check both its extension and actual content. Renaming a PDF to `.docx`, or a GIF to `.png`,
is intentionally rejected.

### A Linux or macOS PDF font cannot be resolved

Install Liberation Sans, DejaVu Sans, or Arial in a standard system font directory. The
exporter checks common Windows, Linux, and macOS locations and embeds the resolved font in
the PDF.

## Known MVP limitations

- Static HTML parsing cannot see runtime-generated elements absent from the template.
- Angular TypeScript behavior is not analyzed.
- Screenshot analysis quality depends on the chosen vision model.
- AI-generated behavioral explanations require review when not explicitly supported by a
  business rule, HTML attribute, or accurate existing documentation.
- Complex formatting from an existing Word manual is not fully preserved.
- Change detection is based on normalized text and edit similarity and can require human
  review.
- A single screenshot and a DOCX input manual are supported in this MVP.

## Future improvements

The boundaries allow later addition of an Angular TypeScript parser, Git integration,
CI/CD generation, documentation version history, navigation mapping, OCR, multiple
screenshots, PDF input manuals, DOCX templates, approval and feedback workflows, database
persistence, a REST API, batch generation, and Git-diff-based updates without changing the
domain model's dependency direction.
