using DeepDrftData;
using DeepDrftData.Data;
using DeepDrftData.Repositories;
using DeepDrftPublic.Client.Services;
using DeepDrftPublic.Services; // DarkModeService namespace (within this host project)
using Microsoft.EntityFrameworkCore;

namespace DeepDrftPublic;

public static class Startup
{
    public static void ConfigureDomainServices(WebApplicationBuilder builder)
    {
        // Add Entity Framework services
        builder.Services.AddDbContext<DeepDrftContext>(options =>
            options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

        // Add Server Prerendering Theming Support
        // DarkModeSettings is registered in DeepDrftPublic.Client.Startup.ConfigureDomainServices
        builder.Services
            .AddHttpContextAccessor()
            .AddScoped<DarkModeService>();

        // Add Track services. TrackManager implements ITrackService for backward compatibility
        // with pages that inject the interface; resolving ITrackService returns the same scoped
        // TrackManager instance so the manager surface (DTO-space) and the service surface
        // (entity-space) share state.
        builder.Services
            .AddScoped<TrackRepository>()
            .AddScoped<TrackManager>()
            .AddScoped<ITrackService>(sp => sp.GetRequiredService<TrackManager>());

        // Override the WASM HTTP-backed ITrackDataService (registered earlier by
        // DeepDrftPublic.Client.Startup.ConfigureDomainServices) with an in-process
        // adapter for SSR prerender. Last registration wins for single-resolution.
        builder.Services.AddScoped<ITrackDataService, TrackDirectDataService>();
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
