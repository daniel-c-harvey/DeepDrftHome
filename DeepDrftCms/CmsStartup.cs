using Microsoft.Extensions.DependencyInjection;

namespace DeepDrftCms;

public static class CmsStartup
{
    public static IServiceCollection AddCmsServices(this IServiceCollection services)
    {
        // CMS-specific services registered here as implementation waves land.
        return services;
    }
}
