using DeepDrftContent.Models;

namespace DeepDrftContent
{
    public static class Startup
    {
        public static void ConfigureDomainServices(WebApplicationBuilder builder)
        {
            // File Database
            builder.Configuration.AddJsonFile("environment/filedatabase.json", optional: false, reloadOnChange: true);
            var fileDatabaseSettings = builder.Configuration.GetSection(nameof(FileDatabaseSettings)).Get<FileDatabaseSettings>();
            if (fileDatabaseSettings is null) { throw new Exception("File database settings are not configured"); }
            builder.Services.AddSingleton(
                    FileDatabase.Services.FileDatabase.FromAsync(
                        fileDatabaseSettings.VaultPath));
        }
    }
}