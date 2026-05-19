using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using DeepDrftData;
using DeepDrftData.Data;
using DeepDrftData.Repositories;
using DeepDrftContent.Data.FileDatabase.Services;
using DeepDrftContent.Data.Processors;
using DeepDrftCli.Services;
using DeepDrftCli.Models;
using NetBlocks.Utilities.Environment;

var builder = Host.CreateApplicationBuilder(args);

// Load configuration from environment/config.json
var connectionsPath = CredentialTools.ResolvePathOrThrow(
    "connections",
    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "environment", "connections.json"));
builder.Configuration.AddJsonFile(connectionsPath, optional: false, reloadOnChange: false);
var cliSettings = builder.Configuration.GetSection(nameof(CliSettings)).Get<CliSettings>();
if (cliSettings is null) { throw new Exception("CLI settings are not configured"); }

// Add logging
builder.Services.AddLogging(configure => configure.AddConsole());

// Add database context
builder.Services.AddDbContext<DeepDrftContext>(options =>
    options.UseNpgsql(cliSettings.ConnectionString));

// Add FileDatabase
builder.Services.AddSingleton<FileDatabase>(provider =>
{
    var logger = provider.GetRequiredService<ILogger<Program>>();
    try
    {
        var fileDatabase = FileDatabase.FromAsync(cliSettings.VaultPath).GetAwaiter().GetResult();
        if (fileDatabase == null)
        {
            logger.LogError("Failed to initialize FileDatabase");
            throw new InvalidOperationException("FileDatabase initialization failed");
        }
        return fileDatabase;
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error initializing FileDatabase");
        throw;
    }
});

// Add services. TrackManager fronts the BlazorBlocks data layer and implements
// ITrackService for legacy consumers; same scoped instance backs both registrations.
builder.Services.AddScoped<TrackRepository>();
builder.Services.AddScoped<TrackManager>();
builder.Services.AddScoped<ITrackService>(sp => sp.GetRequiredService<TrackManager>());
builder.Services.AddScoped<AudioProcessor>();
builder.Services.AddScoped<DeepDrftContent.Data.TrackService>();
builder.Services.AddScoped<CliService>();
builder.Services.AddScoped<GuiService>();

// Build and run
var app = builder.Build();

// Check if GUI mode is requested
if (args.Length > 0 && (args[0].ToLowerInvariant() == "gui" || args[0].ToLowerInvariant() == "--gui"))
{
    // Run GUI mode
    var guiService = app.Services.GetRequiredService<GuiService>();
    await guiService.RunAsync();
}
else
{
    // Run traditional CLI mode
    var cliService = app.Services.GetRequiredService<CliService>();
    await cliService.RunAsync(args);
}
