using DocumentationGenerator.Application.Configuration;
using DocumentationGenerator.Application.Contracts;
using DocumentationGenerator.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Features;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

builder.Services.AddControllersWithViews();
builder.Services.AddDocumentationGenerator(builder.Configuration);
var dataProtectionPath = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "Keys");
Directory.CreateDirectory(dataProtectionPath);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionPath))
    .SetApplicationName("DocumentationGenerator");
var configuredLimit = builder.Configuration.GetValue<long?>($"{UploadOptions.SectionName}:MaxRequestBytes")
                      ?? 35 * 1024 * 1024;
builder.Services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = configuredLimit);

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseRouting();
app.UseAuthorization();
app.MapStaticAssets();

app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

using (var scope = app.Services.CreateScope())
{
    var storage = scope.ServiceProvider.GetRequiredService<IJobStorageService>();
    try
    {
        await storage.CleanupExpiredJobsAsync();
    }
    catch (Exception exception)
    {
        app.Logger.LogWarning(exception, "Expired job cleanup could not be completed during startup");
    }
}

app.Run();

public partial class Program;
