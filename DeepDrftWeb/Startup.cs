using DeepDrftWeb.Data;
using DeepDrftWeb.Data.Repositories;
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

        // Add Track services
        builder.Services.AddScoped<TrackRepository>();
        builder.Services.AddScoped<TrackService>();

    }
}