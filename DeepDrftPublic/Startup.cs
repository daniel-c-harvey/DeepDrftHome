using DeepDrftPublic.Services; // DarkModeService namespace (within this host project)

namespace DeepDrftPublic;

public static class Startup
{
    public static void ConfigureDomainServices(WebApplicationBuilder builder)
    {
        // Add Server Prerendering Theming Support
        // DarkModeSettings is registered in DeepDrftPublic.Client.Startup.ConfigureDomainServices
        builder.Services
            .AddHttpContextAccessor()
            .AddScoped<DarkModeService>();
    }
}
