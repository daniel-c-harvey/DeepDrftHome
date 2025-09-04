using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using DeepDrftWeb.Data;
using DeepDrftWeb.Data.Repositories;
using DeepDrftContent.FileDatabase.Services;
using DeepDrftContent.Processors;
using DeepDrftContent.Services;
using DeepDrftCli.Services;

var builder = Host.CreateApplicationBuilder(args);

// Add configuration
var appDirectory = AppContext.BaseDirectory;
var configPath = Path.Combine(appDirectory, "appsettings.json");
builder.Configuration.AddJsonFile(configPath, optional: false, reloadOnChange: true);

// Add logging
builder.Services.AddLogging(configure => configure.AddConsole());

// Add database context
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrEmpty(connectionString))
    throw new InvalidOperationException("DefaultConnection not found in configuration");

builder.Services.AddDbContext<DeepDrftContext>(options =>
    options.UseSqlite(connectionString));

// Add FileDatabase
builder.Services.AddSingleton<FileDatabase>(provider =>
{
    var logger = provider.GetRequiredService<ILogger<Program>>();
    var configuration = provider.GetRequiredService<IConfiguration>();
    try
    {
        var vaultPath = configuration["FileDatabaseSettings:VaultPath"];
        if (string.IsNullOrEmpty(vaultPath))
            throw new InvalidOperationException("FileDatabaseSettings:VaultPath not found in configuration");
            
        var fileDatabase = FileDatabase.FromAsync(vaultPath).GetAwaiter().GetResult();
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

// Build and run
var app = builder.Build();

// Get the CLI service and run
var cliService = app.Services.GetRequiredService<CliService>();
await cliService.RunAsync(args);