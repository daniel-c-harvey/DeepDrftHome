using DeepDrftContent;
using DeepDrftContent.Services.FileDatabase.Services;
using DeepDrftContent.Middleware;
using DeepDrftContent.Models;
using Microsoft.AspNetCore.HttpOverrides;

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

// Load API key configuration
builder.Configuration.AddJsonFile("environment/apikey.json", optional: false, reloadOnChange: true);
var apiKeySettings = builder.Configuration.GetSection(nameof(ApiKeySettings)).Get<ApiKeySettings>();
if (apiKeySettings is null) { throw new Exception("API key settings are not configured"); }

// Configure forwarded headers for reverse proxy support
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    // Trust any proxy (nginx) - in production, specify known proxy networks
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

// Configure the HTTP request pipeline.
// Use forwarded headers before other middleware
app.UseForwardedHeaders();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
else
{
    // Only use HTTPS redirection if not behind a reverse proxy
    var forwardedProto = app.Services.GetService<IConfiguration>()?["ForwardedHeaders:DisableHttpsRedirection"];
    if (string.IsNullOrEmpty(forwardedProto) || !bool.Parse(forwardedProto))
    {
        app.UseHttpsRedirection();
    }
}

app.UseCors("ContentApiPolicy");
app.UseApiKeyAuthentication(apiKeySettings.ApiKey);
app.UseAuthorization();

app.MapControllers();

app.Run();