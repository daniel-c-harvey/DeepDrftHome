using DeepDrftPublic.Client.Clients;
using DeepDrftPublic.Client.Common;
using DeepDrftPublic.Client.Services;
using DeepDrftPublic.Client.ViewModels;
using Microsoft.AspNetCore.Http;

namespace DeepDrftPublic.Client;

public static class Startup
{
    public static void ConfigureDomainServices(IServiceCollection services)
    {
        // Theme Support
        services.AddScoped<DarkModeSettings>();
        services.AddScoped<DarkModeCookieService>();

        // Track Client. The HTTP-backed ITrackDataService registration here is the WASM
        // default; the server host overrides it with an in-process implementation after
        // this method runs, so SSR prerender skips the loopback hop.
        services.AddScoped<TrackClient>();
        services.AddScoped<ITrackDataService, TrackClientDataService>();
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