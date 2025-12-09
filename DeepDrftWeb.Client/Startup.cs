using DeepDrftWeb.Client.Clients;
using DeepDrftWeb.Client.Common;
using DeepDrftWeb.Client.Services;
using DeepDrftWeb.Client.ViewModels;
using Microsoft.AspNetCore.Http;

namespace DeepDrftWeb.Client;

public static class Startup
{
    public static void ConfigureDomainServices(IServiceCollection services)
    {
        // Theme Support
        services.AddScoped<DarkModeSettings>();
        services.AddScoped<DarkModeCookieService>();

        // Track Client
        services.AddScoped<TrackClient>();
        services.AddScoped<TracksViewModel>();
    }

    public static void ConfigureApiHttpClient(IServiceCollection services, string baseAddress)
    {
        services.AddHttpClient("DeepDrft.API", client => 
        {
            client.BaseAddress = new Uri(baseAddress);
        });
    }

    public static void ConfigureContentServices(IServiceCollection services, string contentApiUrl)
    {
        services.AddHttpClient("DeepDrft.Content", client => 
        {
            client.BaseAddress = new Uri(contentApiUrl);
        });
        services.AddScoped<TrackMediaClient>();
        services.AddScoped<AudioInteropService>();
    }
}