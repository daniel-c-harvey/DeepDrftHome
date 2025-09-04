using DeepDrftContent;
using DeepDrftContent.FileDatabase.Services;
using DeepDrftContent.Middleware;
using DeepDrftContent.Models;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
await Startup.ConfigureDomainServices(builder);

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Load API key configuration
builder.Configuration.AddJsonFile("environment/apikey.json", optional: false, reloadOnChange: true);
var apiKeySettings = builder.Configuration.GetSection(nameof(ApiKeySettings)).Get<ApiKeySettings>();
if (apiKeySettings is null) { throw new Exception("API key settings are not configured"); }

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseApiKeyAuthentication(apiKeySettings.ApiKey);
app.UseAuthorization();

app.MapControllers();

app.Run();