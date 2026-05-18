using Microsoft.Extensions.DependencyInjection;

namespace DeepDrftCms;

public static class CmsStartup
{
    public static IServiceCollection AddCmsServices(this IServiceCollection services)
    {
        // CMS-specific services registered here as implementation waves land.
        //
        // Note: ITrackService (and its dependencies: TrackRepository, DeepDrftContext) is registered
        // by DeepDrftWeb.Startup.ConfigureDomainServices, not here. The CMS RCL runs inside the
        // DeepDrftWeb host, which wires those up before this method is called. A standalone CMS
        // host would need to register ITrackService separately.
        return services;
    }
}
