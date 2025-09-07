using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using DeepDrftWeb.Services.Data;
using DeepDrftWeb.Services.Repositories;
using DeepDrftContent.Services.FileDatabase.Services;
using DeepDrftContent.Services.Processors;
using DeepDrftCli.Services;
using DeepDrftCli.Models;

var builder = Host.CreateApplicationBuilder(args);

// Load configuration from environment/config.json
builder.Configuration.AddJsonFile($"{AppDomain.CurrentDomain.BaseDirectory}environment/connections.json", optional: false, reloadOnChange: true);
var cliSettings = builder.Configuration.GetSection(nameof(CliSettings)).Get<CliSettings>();
if (cliSettings is null) { throw new Exception("CLI settings are not configured"); }

// Add logging
builder.Services.AddLogging(configure => configure.AddConsole());

// Add database context
builder.Services.AddDbContext<DeepDrftContext>(options =>
    options.UseSqlite(cliSettings.ConnectionString));

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

// Add services
builder.Services.AddScoped<TrackRepository>();
builder.Services.AddScoped<DeepDrftWeb.Services.TrackService>();
builder.Services.AddScoped<AudioProcessor>();
builder.Services.AddScoped<DeepDrftContent.Services.TrackService>();
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