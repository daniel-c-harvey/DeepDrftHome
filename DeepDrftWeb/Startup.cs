using DeepDrftWeb.Services.Data;
using DeepDrftWeb.Services.Repositories;
using DeepDrftWeb.Services;
using Microsoft.EntityFrameworkCore;

namespace DeepDrftWeb;

public static class Startup
{
    public static void ConfigureDomainServices(WebApplicationBuilder builder)
    {
        // Add Entity Framework services
        builder.Services.AddDbContext<DeepDrftContext>(options =>
            options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

        // Add Server Prerendering Theming Support
        // DarkModeSettings is registered in DeepDrftWeb.Client.Startup.ConfigureDomainServices
        builder.Services
            .AddHttpContextAccessor()
            .AddScoped<DarkModeService>();
        
        // Add Track services
        builder.Services
            .AddScoped<TrackRepository>()
            .AddScoped<TrackService>();
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