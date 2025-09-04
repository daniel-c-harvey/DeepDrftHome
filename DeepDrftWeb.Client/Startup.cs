using DeepDrftWeb.Client.Clients;
using DeepDrftWeb.Client.ViewModels;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using NetBlocks.Models;

namespace DeepDrftWeb.Client;

public static class Startup
{
    public static void ConfigureDomainServices(IServiceCollection services)
    {
        // Track Client
        services.AddScoped<TrackClient>();
        services.AddScoped<TrackGalleryViewModel>();
    }
}