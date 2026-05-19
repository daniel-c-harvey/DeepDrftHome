using DeepDrftData;
using DeepDrftData.Data;
using DeepDrftData.Repositories;
using DeepDrftWeb.Services; // DarkModeService namespace (within this host project)
using Microsoft.EntityFrameworkCore;

namespace DeepDrftWeb;

public static class Startup
{
    /// <summary>
    /// Named HttpClient used by CMS controllers to call DeepDrftContent's ApiKey-protected endpoints.
    /// Distinct from the public WASM-facing "DeepDrft.Content" client so the API key never reaches
    /// the browser. Configured server-side only.
    /// </summary>
    public const string ContentCmsHttpClientName = "DeepDrft.Content.Cms";

    public static void ConfigureDomainServices(WebApplicationBuilder builder)
    {
        // Add Entity Framework services
        builder.Services.AddDbContext<DeepDrftContext>(options =>
            options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

        // Add Server Prerendering Theming Support
        // DarkModeSettings is registered in DeepDrftWeb.Client.Startup.ConfigureDomainServices
        builder.Services
            .AddHttpContextAccessor()
            .AddScoped<DarkModeService>();

        // Add Track services. TrackManager implements ITrackService for backward compatibility
        // with controllers and CMS pages that inject the interface; resolving ITrackService
        // returns the same scoped TrackManager instance so the manager surface (DTO-space)
        // and the service surface (entity-space) share state.
        builder.Services
            .AddScoped<TrackRepository>()
            .AddScoped<TrackManager>()
            .AddScoped<ITrackService>(sp => sp.GetRequiredService<TrackManager>());

        // CMS → DeepDrftContent client. The API key is required up front (no lazy resolution)
        // so a misconfiguration surfaces at startup instead of on the first delete attempt.
        var contentApiUrl = builder.Configuration["ApiUrls:ContentApi"]
            ?? throw new InvalidOperationException("ApiUrls:ContentApi is required");
        var contentApiKey = builder.Configuration["DeepDrftContent:ApiKey"]
            ?? throw new InvalidOperationException("DeepDrftContent:ApiKey is required (see environment/apikey.json)");

        builder.Services.AddHttpClient(ContentCmsHttpClientName, client =>
        {
            client.BaseAddress = new Uri(contentApiUrl);
            client.DefaultRequestHeaders.Add("ApiKey", contentApiKey);
        });
    }

    public static string GetKestrelUrl(this WebApplicationBuilder builder)
    {
        // Check all the places Kestrel URL can be configured
        var urls = builder.Configuration["ASPNETCORE_URLS"]
                   ?? builder.Configuration["urls"];

        if (!string.IsNullOrEmpty(urls))
        {
            return urls.Split(';')[0].Trim();
        }

        // Check Kestrel endpoints configuration
        var kestrelSection = builder.Configuration.GetSection("Kestrel:Endpoints");
        var firstEndpoint = kestrelSection.GetChildren().FirstOrDefault();
        var endpointUrl = firstEndpoint?["Url"];

        if (!string.IsNullOrEmpty(endpointUrl))
        {
            return endpointUrl;
        }

        // ASP.NET Core defaults
        return builder.Environment.IsDevelopment()
            ? "https://localhost:5001"
            : "http://localhost:5000";
    }
}
