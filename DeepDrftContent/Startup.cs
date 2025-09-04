using DeepDrftContent.Constants;
using DeepDrftContent.FileDatabase.Models;
using DeepDrftContent.FileDatabase.Services;
using DeepDrftContent.Models;

namespace DeepDrftContent
{
    public static class Startup
    {
        public static async Task ConfigureDomainServices(WebApplicationBuilder builder)
        {
            // File Database
            builder.Configuration.AddJsonFile("environment/filedatabase.json", optional: false, reloadOnChange: true);
            var fileDatabaseSettings = builder.Configuration.GetSection(nameof(FileDatabaseSettings)).Get<FileDatabaseSettings>();
            if (fileDatabaseSettings is null) { throw new Exception("File database settings are not configured"); }

            var fileDatabase = await FileDatabase.Services.FileDatabase.FromAsync(fileDatabaseSettings.VaultPath);
            if (fileDatabase is null) { throw new Exception("Unable to initialize file database"); } 
            builder.Services.AddSingleton(fileDatabase);
            await InitializeTrackVault(fileDatabase);
        }

        private static async Task InitializeTrackVault(FileDatabase.Services.FileDatabase fileDatabase)
        {
            if (!fileDatabase.HasVault(VaultConstants.Tracks))
            {
                await fileDatabase.CreateVaultAsync(VaultConstants.Tracks, MediaVaultType.Audio);
            }
        }
    }
}