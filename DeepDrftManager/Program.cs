using AuthBlocksLib;
using AuthBlocksLib.Options;
using DeepDrftManager.Components;
using DeepDrftManager.Services;
using Microsoft.AspNetCore.HttpOverrides;
using MudBlazor.Services;
using NetBlocks.Utilities.Environment;

var builder = WebApplication.CreateBuilder(args);

// Required credential files — must exist before the app will start.
// Production secrets stay gitignored; the *.example.json templates at the project root show the shape.
//   - environment/api.json: { "Api": { "ContentApiUrl": "...", "ContentApiKey": "..."  } }
//   - environment/connections.json: { "ConnectionStrings": { "DefaultConnection": "...", "Auth": "..." } }
//   - environment/authblocks.json:  { "AuthBlocks": { "Jwt": {...}, "Email": {...}, "Admin": {...} } }
// Content API key — consumed by CmsTrackService for the upload proxy and the vault-delete client.
var apiPath = CredentialTools.ResolvePathOrThrow("api", "environment/api.json");
builder.Configuration.AddJsonFile(apiPath, optional: false, reloadOnChange: false);

var connectionsPath = CredentialTools.ResolvePathOrThrow("connections", "environment/connections.json");
builder.Configuration.AddJsonFile(connectionsPath, optional: false, reloadOnChange: false);

var authBlocksPath = CredentialTools.ResolvePathOrThrow("authblocks", "environment/authblocks.json");
builder.Configuration.AddJsonFile(authBlocksPath, optional: false, reloadOnChange: false);

// MudBlazor.
builder.Services.AddMudServices();

// CMS track operations (read + mutate). Every track read and write goes over HTTP to the
// DeepDrftAPI API via the named clients below — the Manager holds no in-process data layer.
builder.Services.AddScoped<ICmsTrackService, CmsTrackService>();

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

// Named HttpClient for unauthenticated Content API calls (CmsTrackService proxying WAV data
// to DeepDrftAPI's POST api/track/upload). API key added per-request by the service.
var contentApiUrl = builder.Configuration["Api:ContentApiUrl"]
    ?? throw new InvalidOperationException("Api:ContentApiUrl is required");
builder.Services.AddHttpClient("DeepDrft.Content", client =>
{
    client.BaseAddress = new Uri(contentApiUrl);
});

// Named HttpClient for ApiKey-protected Content API calls (CmsTrackService's vault delete).
// API key baked into the default request headers so callers need not add it manually.
var contentApiKey = builder.Configuration["Api:ContentApiKey"]
    ?? throw new InvalidOperationException("Api:ContentApiKey is required");
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
app.UseAntiforgery();
app.UseAuthorization();

app.MapStaticAssets();

// Mount AuthBlocks API surface (/api/auth/*, /api/users/*, etc.) and the AuthBlocksWeb
// Razor pages (/account/login, /account/logout).
app.MapAuthBlocks();

// Blazor page authorization is owned by AuthorizeRouteView in Routes.razor, not
// ASP.NET Core endpoint authorization. AuthBlocks tokens live in browser localStorage
// (read via JS interop by JwtAuthenticationStateProvider), so the JWT never reaches
// the server on a navigation request. Without AllowAnonymous here, the JwtBearer
// challenge for an unauthenticated nav returns 401 before the Blazor router runs,
// short-circuiting the NotAuthorized -> RedirectToLogin path. JWT enforcement
// remains in force for the AuthBlocks API surface (MapAuthBlocks).
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(typeof(AuthBlocksWeb._Imports).Assembly)
    .AllowAnonymous();

app.Run();

// Local helper — mirrors DeepDrftPublic.Startup.GetKestrelUrl. Kept inline because this host's
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
