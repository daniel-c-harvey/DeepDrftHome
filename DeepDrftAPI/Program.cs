using AuthBlocksLib;
using AuthBlocksLib.Options;
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

// Required credential files — must exist before the app will start.
// Production secrets stay gitignored; the *.example.json templates at the project root show the shape.
//   - environment/filedatabase.json: { "FileDatabaseSettings": { "VaultPath": "..." } }
//   - environment/apikey.json:       { "ApiKeySettings": { "ApiKey": "..." } }
//   - environment/connections.json:  { "ConnectionStrings": { "DefaultConnection": "...", "Auth": "..." } }
//   - environment/authblocks.json:   { "AuthBlocks": { "Jwt": {...}, "Email": {...}, "Admin": {...} } }
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

// AuthBlocks: JWT Bearer auth, Identity, EF schema, role + admin seeding. This API host owns the
// AuthBlocks API surface (registration, migration/seed, endpoint mounting). The Manager keeps only
// web-side auth (AuthBlocksWeb) and never holds the signing secret, email creds, or admin creds.
// Auth schema runs in its own database (separate from DefaultConnection by design).
var authBlocksPath = CredentialTools.ResolvePathOrThrow("authblocks", "environment/authblocks.json");
builder.Configuration.AddJsonFile(authBlocksPath, optional: false, reloadOnChange: false);

builder.Services.AddAuthBlocks(options =>
{
    options.ConnectionString = builder.Configuration.GetConnectionString("Auth")
        ?? throw new InvalidOperationException("ConnectionStrings:Auth is required");
    options.ApplicationName  = "DeepDrft";
    options.SupportEmail     = builder.Configuration["AuthBlocks:SupportEmail"] ?? "admin@deepdrft.com";

    options.JwtSettings.Secret   = builder.Configuration["AuthBlocks:Jwt:Secret"]
        ?? throw new InvalidOperationException("AuthBlocks:Jwt:Secret is required");
    options.JwtSettings.Issuer   = builder.Configuration["AuthBlocks:Jwt:Issuer"]
        ?? throw new InvalidOperationException("AuthBlocks:Jwt:Issuer is required");
    options.JwtSettings.Audience = builder.Configuration["AuthBlocks:Jwt:Audience"]
        ?? throw new InvalidOperationException("AuthBlocks:Jwt:Audience is required");

    options.EmailConnection.Host  = builder.Configuration["AuthBlocks:Email:Host"]
        ?? throw new InvalidOperationException("AuthBlocks:Email:Host is required");
    options.EmailConnection.Token = builder.Configuration["AuthBlocks:Email:Token"]
        ?? throw new InvalidOperationException("AuthBlocks:Email:Token is required");

    options.AdminUserSettings = new AdminUserSettings
    {
        UserName = builder.Configuration["AuthBlocks:Admin:UserName"]
            ?? throw new InvalidOperationException("AuthBlocks:Admin:UserName is required"),
        Email    = builder.Configuration["AuthBlocks:Admin:Email"]
            ?? throw new InvalidOperationException("AuthBlocks:Admin:Email is required"),
        Password = builder.Configuration["AuthBlocks:Admin:Password"]
            ?? throw new InvalidOperationException("AuthBlocks:Admin:Password is required")
    };
});

// Configure forwarded headers for reverse proxy support
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    // Trust any proxy (nginx) - in production, specify known proxy networks
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

// Apply AuthBlocks EF migrations, seed system roles, seed admin user on first boot.
await app.Services.UseAuthBlocksStartupAsync();

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

// ApiKey middleware only enforces on endpoints tagged [ApiKeyAuthorize] (the track surface); it
// passes all other endpoints through. JWT auth/authorization gate the AuthBlocks endpoints, which
// carry no [ApiKeyAuthorize] metadata — the two schemes are orthogonal and do not interfere.
app.UseApiKeyAuthentication(apiKeySettings.ApiKey);
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Mount the AuthBlocks API surface (/api/auth/*, /api/users/*, /api/roles/*, /api/user-roles/*,
// /api/pending-registrations/*). Protected routes require the JWT bearer scheme registered above.
app.MapAuthBlocks();

app.Run();