using DeepDrftAPI;
using DeepDrftAPI.Middleware;
using DeepDrftAPI.Models;
using DeepDrftAPI.Services;
using DeepDrftData;
using DeepDrftData.Data;
using DeepDrftData.Repositories;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using NetBlocks.Utilities.Environment;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
await Startup.ConfigureDomainServices(builder);

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Add CORS policy using configured origins
var corsSettings = builder.Configuration.GetSection(nameof(CorsSettings)).Get<CorsSettings>();
if (corsSettings?.AllowedOrigins == null || corsSettings.AllowedOrigins.Length == 0)
{
    throw new Exception("CorsSettings.AllowedOrigins configuration is required for CORS policy");
}

builder.Services.AddCors(options =>
{
    options.AddPolicy("ContentApiPolicy", policy =>
    {
        policy.WithOrigins(corsSettings.AllowedOrigins)
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

// Load API key via CredentialTools (dev: environment/apikey.json; prod: CREDENTIALS_DIRECTORY/apikey)
var apiKeyPath = CredentialTools.ResolvePathOrThrow("apikey", "environment/apikey.json");
builder.Configuration.AddJsonFile(apiKeyPath, optional: false, reloadOnChange: false);
var apiKeySettings = builder.Configuration.GetSection(nameof(ApiKeySettings)).Get<ApiKeySettings>();
if (apiKeySettings is null) { throw new Exception("API key settings are not configured"); }

// SQL connection string — DeepDrftAPI now owns both vault (FileDatabase) and SQL metadata.
var connectionsPath = CredentialTools.ResolvePathOrThrow("connections", "environment/connections.json");
builder.Configuration.AddJsonFile(connectionsPath, optional: false, reloadOnChange: false);

// SQL metadata domain — DbContext + repository + manager (scoped; DbContext is not thread-safe).
// UnifiedTrackService orchestrates the two databases and is the single authority over track data.
builder.Services.AddDbContext<DeepDrftContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services
    .AddScoped<TrackRepository>()
    .AddScoped<TrackManager>()
    .AddScoped<ITrackService>(sp => sp.GetRequiredService<TrackManager>());
builder.Services.AddScoped<UnifiedTrackService>();

// Configure forwarded headers for reverse proxy support
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    // Trust any proxy (nginx) - in production, specify known proxy networks
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

if (app.Environment.IsProduction())
{
    // Use forwarded headers before other middleware
    app.UseForwardedHeaders();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors("ContentApiPolicy");
app.UseApiKeyAuthentication(apiKeySettings.ApiKey);

app.MapControllers();

app.Run();