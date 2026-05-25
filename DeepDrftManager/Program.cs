using DeepDrftManager.Components;
using DeepDrftManager.Services;
using Microsoft.AspNetCore.HttpOverrides;
using MudBlazor.Services;
using NetBlocks.Utilities.Environment;

var builder = WebApplication.CreateBuilder(args);

// Required credential files — must exist before the app will start.
// Production secrets stay gitignored; the *.example.json templates at the project root show the shape.
//   - environment/api.json: { "Api": { "ContentApiUrl": "...", "ContentApiKey": "..."  } }
// The Manager hosts only web-side auth (AuthBlocksWeb), which talks to the AuthBlocks API on
// DeepDrftAPI. It holds no JWT signing secret, email creds, admin creds, or Auth connection string —
// those moved to DeepDrftAPI's environment/authblocks.json + environment/connections.json.
// Content API key — consumed by CmsTrackService for the upload proxy and the vault-delete client.
var apiPath = CredentialTools.ResolvePathOrThrow("api", "environment/api.json");
builder.Configuration.AddJsonFile(apiPath, optional: false, reloadOnChange: false);

// MudBlazor.
builder.Services.AddMudServices();

// CMS track operations (read + mutate). Every track read and write goes over HTTP to the
// DeepDrftAPI API via the named clients below — the Manager holds no in-process data layer.
builder.Services.AddScoped<ICmsTrackService, CmsTrackService>();

// AuthBlocksWeb: server-side cascading auth state plus the JWT client services used by the
// /account/login + /account/logout Razor pages that ship in the AuthBlocksWeb RCL.
// The auth API lives on DeepDrftAPI, so pass its URL — not Manager's own Kestrel URL.
var contentApiUrl = builder.Configuration["Api:ContentApiUrl"]
    ?? throw new InvalidOperationException("Api:ContentApiUrl is required");
AuthBlocksWeb.Startup.ConfigureAuthServices(builder.Services, contentApiUrl);

// Named HttpClient for unauthenticated Content API calls (CmsTrackService proxying WAV data
// to DeepDrftAPI's POST api/track/upload). API key added per-request by the service.
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

// The AuthBlocks API surface (/api/auth/*, /api/users/*, etc.) now lives on DeepDrftAPI; this host
// only renders the AuthBlocksWeb Razor pages (/account/login, /account/logout), which call that API.
// Blazor page authorization is owned by AuthorizeRouteView in Routes.razor, not ASP.NET Core
// endpoint authorization. AuthBlocks tokens live in browser localStorage (read via JS interop by
// JwtAuthenticationStateProvider), so the JWT never reaches the server on a navigation request.
// Without AllowAnonymous here, the cookie/JwtBearer challenge for an unauthenticated nav returns 401
// before the Blazor router runs, short-circuiting the NotAuthorized -> RedirectToLogin path.
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(typeof(AuthBlocksWeb._Imports).Assembly)
    .AllowAnonymous();

app.Run();
