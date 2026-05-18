using AuthBlocksLib;
using AuthBlocksLib.Options;
using DeepDrftCms;
using DeepDrftWeb;
using MudBlazor.Services;
using DeepDrftWeb.Components;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

// Add MudBlazor services
builder.Services.AddMudServices();

builder.Services.AddCmsServices();

var baseUrl = builder.GetKestrelUrl();
var contentApiUrl = builder.Configuration["ApiUrls:ContentApi"] ?? throw new Exception("Content API URL is not configured");

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

// AuthBlocksWeb: Blazor JWT client services (auth API is mounted on this same host via MapAuthBlocks).
// AuthBlocksWeb.Startup.ConfigureAuthServices registers AddCascadingAuthenticationState server-side.
AuthBlocksWeb.Startup.ConfigureAuthServices(builder.Services, baseUrl);

DeepDrftWeb.Client.Startup.ConfigureApiHttpClient(builder.Services, baseUrl);
DeepDrftWeb.Client.Startup.ConfigureDomainServices(builder.Services);
DeepDrftWeb.Client.Startup.ConfigureContentServices(builder.Services, contentApiUrl);

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

// Apply AuthBlocks EF migrations, seed system roles, seed admin user on first boot.
await app.Services.UseAuthBlocksStartupAsync();

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

app.UseAuthentication();
app.UseAuthorization();

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
app.MapAuthBlocks(); // registers /api/auth/*, /api/users/*, /api/roles/*, /api/user-roles/*, /api/pending-registrations/*
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(
        typeof(DeepDrftWeb.Client._Imports).Assembly,
        typeof(DeepDrftCms._Imports).Assembly,
        typeof(AuthBlocksWeb._Imports).Assembly);   // exposes /account/login, /account/logout


app.Run();
