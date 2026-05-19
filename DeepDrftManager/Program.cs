using AuthBlocksLib;
using AuthBlocksLib.Options;
using DeepDrftCms;
using DeepDrftData;
using DeepDrftData.Data;
using DeepDrftData.Repositories;
using DeepDrftManager.Components;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using NetBlocks.Utilities.Environment;

var builder = WebApplication.CreateBuilder(args);

// Required credential files — must exist before the app will start.
// Production secrets stay gitignored; the *.example.json templates at the project root show the shape.
//   - environment/apikey.json:      { "DeepDrftContent": { "ApiKey": "..." } }
//   - environment/connections.json: { "ConnectionStrings": { "DefaultConnection": "...", "Auth": "..." } }
//   - environment/authblocks.json:  { "AuthBlocks": { "Jwt": {...}, "Email": {...}, "Admin": {...} } }
// Content API key — not consumed by this host in Phase 1. Required by the CredentialTools
// pattern (the file must exist); will be used by CmsUploadController when it migrates here.
var apiKeyPath = CredentialTools.ResolvePathOrThrow("apikey", "environment/apikey.json");
builder.Configuration.AddJsonFile(apiKeyPath, optional: false, reloadOnChange: false);

var connectionsPath = CredentialTools.ResolvePathOrThrow("connections", "environment/connections.json");
builder.Configuration.AddJsonFile(connectionsPath, optional: false, reloadOnChange: false);

var authBlocksPath = CredentialTools.ResolvePathOrThrow("authblocks", "environment/authblocks.json");
builder.Configuration.AddJsonFile(authBlocksPath, optional: false, reloadOnChange: false);

// MudBlazor.
builder.Services.AddMudServices();

// CMS-specific services (currently a no-op placeholder; reserved for future RCL additions).
builder.Services.AddCmsServices();

// SQL metadata domain — DbContext + repository + manager. The CMS pages inject ITrackService
// and resolve the same scoped TrackManager instance, so the DTO and entity surfaces share state.
builder.Services.AddDbContext<DeepDrftContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services
    .AddScoped<TrackRepository>()
    .AddScoped<TrackManager>()
    .AddScoped<ITrackService>(sp => sp.GetRequiredService<TrackManager>());

// AuthBlocks: JWT Bearer auth, Identity, EF schema, admin seeding.
// Auth schema runs in its own database (separate from DefaultConnection by design).
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

// AuthBlocksWeb: server-side cascading auth state plus the JWT client services used by the
// /account/login + /account/logout Razor pages that ship in the AuthBlocksWeb RCL.
var baseUrl = GetKestrelUrl(builder);
AuthBlocksWeb.Startup.ConfigureAuthServices(builder.Services, baseUrl);

// Named HttpClient used by CMS pages for auth API calls (AuthBlocks surface on this host).
var apiHostUrl = builder.Configuration["ApiUrls:ApiHost"]
    ?? throw new InvalidOperationException("ApiUrls:ApiHost is required");
builder.Services.AddHttpClient("DeepDrft.API", client =>
{
    client.BaseAddress = new Uri(apiHostUrl);
});

// Named HttpClient for unauthenticated Content API calls (e.g. CmsUploadController proxying WAV
// data to DeepDrftContent's POST api/track/upload). API key added per-request by the controller.
var contentApiUrl = builder.Configuration["ApiUrls:ContentApi"]
    ?? throw new InvalidOperationException("ApiUrls:ContentApi is required");
builder.Services.AddHttpClient("DeepDrft.Content", client =>
{
    client.BaseAddress = new Uri(contentApiUrl);
});

// Named HttpClient for ApiKey-protected Content API calls (e.g. CmsDeleteController's vault
// delete). API key baked into the default request headers so callers need not add it manually.
var contentApiKey = builder.Configuration["DeepDrftContent:ApiKey"]
    ?? throw new InvalidOperationException("DeepDrftContent:ApiKey is required");
builder.Services.AddHttpClient("DeepDrft.Content.Cms", client =>
{
    client.BaseAddress = new Uri(contentApiUrl);
    client.DefaultRequestHeaders.Add("ApiKey", contentApiKey);
});

// Reverse-proxy support (nginx in production).
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedProto |
        ForwardedHeaders.XForwardedHost;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// Controllers: discovers CMS mutation controllers (CmsUploadController, CmsEditController,
// CmsDeleteController) and the AuthBlocks surface. Matches DeepDrftWeb precedent.
builder.Services.AddControllers();

// InteractiveServer only — no WASM render mode on the CMS host.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// SignalR tuning for InteractiveServer circuits. Long-running CMS operations (upload progress,
// large table loads) benefit from tighter keepalive and detailed errors in dev.
builder.Services.AddSignalR(options =>
{
    if (builder.Environment.IsDevelopment())
    {
        options.EnableDetailedErrors = true;
        options.KeepAliveInterval = TimeSpan.FromSeconds(10);
        options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
    }
});

var app = builder.Build();

// Apply AuthBlocks EF migrations, seed system roles, seed admin user on first boot.
await app.Services.UseAuthBlocksStartupAsync();

app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();

    var disableHttpsRedirection = app.Configuration.GetValue<bool>("ForwardedHeaders:DisableHttpsRedirection");
    if (!disableHttpsRedirection)
    {
        app.UseHttpsRedirection();
    }
}

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();

// Mount AuthBlocks API surface (/api/auth/*, /api/users/*, etc.) and the AuthBlocksWeb
// Razor pages (/account/login, /account/logout).
app.MapAuthBlocks();

// Mounts CMS mutation controllers (CmsUploadController, CmsEditController, CmsDeleteController).
app.MapControllers();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(
        typeof(DeepDrftCms._Imports).Assembly,
        typeof(AuthBlocksWeb._Imports).Assembly);

app.Run();

// Local helper — mirrors DeepDrftWeb.Startup.GetKestrelUrl. Kept inline because this host's
// only consumer is right here; promoting to a shared library would be premature.
static string GetKestrelUrl(WebApplicationBuilder builder)
{
    var urls = builder.Configuration["ASPNETCORE_URLS"]
               ?? builder.Configuration["urls"];

    if (!string.IsNullOrEmpty(urls))
    {
        return urls.Split(';')[0].Trim();
    }

    var firstEndpoint = builder.Configuration.GetSection("Kestrel:Endpoints").GetChildren().FirstOrDefault();
    var endpointUrl = firstEndpoint?["Url"];

    if (!string.IsNullOrEmpty(endpointUrl))
    {
        return endpointUrl;
    }

    return builder.Environment.IsDevelopment()
        ? "https://localhost:5004"
        : "http://localhost:5000";
}
