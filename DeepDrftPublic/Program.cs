using DeepDrftPublic;
using MudBlazor.Services;
using DeepDrftPublic.Components;
using Microsoft.AspNetCore.HttpOverrides;
using NetBlocks.Utilities.Environment;

var builder = WebApplication.CreateBuilder(args);

// Add MudBlazor services
builder.Services.AddMudServices();

// Required credential files — must exist before the app will start.
// In dev: create the files under DeepDrftPublic/environment/ (gitignored).
// In prod: systemd CREDENTIALS_DIRECTORY points to encrypted credential blobs.
//   - environment/connections.json: { "ConnectionStrings": { "DefaultConnection": "..." } }
// AuthBlocks and the DeepDrftAPI API key now live on DeepDrftManager;
// the public host has no auth surface and no CMS upload proxy.
var connectionsPath = CredentialTools.ResolvePathOrThrow("connections", "environment/connections.json");
builder.Configuration.AddJsonFile(connectionsPath, optional: false, reloadOnChange: false);

var contentApiUrl = builder.Configuration["ApiUrls:ContentApi"] ?? throw new Exception("Content API URL is not configured");

DeepDrftPublic.Client.Startup.ConfigureApiHttpClient(builder.Services, builder.GetKestrelUrl());
DeepDrftPublic.Client.Startup.ConfigureDomainServices(builder.Services);
DeepDrftPublic.Client.Startup.ConfigureContentServices(builder.Services, contentApiUrl);

Startup.ConfigureDomainServices(builder);

builder.Services.AddControllers();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();

// Configure SignalR for better circuit cleanup
builder.Services.AddSignalR(options =>
{
    if (builder.Environment.IsDevelopment())
    {
        options.EnableDetailedErrors = true;
        options.KeepAliveInterval = TimeSpan.FromSeconds(10);
        options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
    }
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

// Configure the HTTP request pipeline.
// Use forwarded headers before other middleware
app.UseForwardedHeaders();

if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();

    // Only use HTTPS redirection if not behind a reverse proxy
    var disableHttpsRedirection = app.Configuration.GetValue<bool>("ForwardedHeaders:DisableHttpsRedirection");
    if (!disableHttpsRedirection)
    {
        app.UseHttpsRedirection();
    }
}

// Antiforgery is required by Blazor form handling. Authentication / authorization
// middleware is intentionally absent — this host is fully anonymous.
app.UseAntiforgery();

// Configure cache headers for Blazor WebAssembly assets
if (app.Environment.IsDevelopment())
{
    app.Use(async (context, next) =>
    {
        if (context.Request.Path.StartsWithSegments("/_framework") ||
            context.Request.Path.StartsWithSegments("/_content"))
        {
            context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
            context.Response.Headers.Pragma = "no-cache";
            context.Response.Headers.Expires = "0";
        }
        await next();
    });
}

app.MapStaticAssets();

// Serve TypeScript source files for debugging in development
if (app.Environment.IsDevelopment())
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(
            Path.Combine(app.Environment.ContentRootPath, "Interop")),
        RequestPath = "/Interop"
    });
}

app.MapControllers();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(DeepDrftPublic.Client._Imports).Assembly);


app.Run();
