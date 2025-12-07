using DeepDrftContent.Services.Audio;
using DeepDrftContent.Services.Constants;
using DeepDrftContent.Services.FileDatabase.Models;
using DeepDrftContent.Services.FileDatabase.Services;
using DeepDrftContent.Models;

namespace DeepDrftContent
{
    public static class Startup
    {
        public static async Task ConfigureDomainServices(WebApplicationBuilder builder)
        {
            // Audio services
            builder.Services.AddSingleton<WavOffsetService>();

            // File Database
            builder.Configuration.AddJsonFile("environment/filedatabase.json", optional: false, reloadOnChange: true);
            var fileDatabaseSettings = builder.Configuration.GetSection(nameof(FileDatabaseSettings)).Get<FileDatabaseSettings>();
            if (fileDatabaseSettings is null) { throw new Exception("File database settings are not configured"); }

            var fileDatabase = await FileDatabase.FromAsync(fileDatabaseSettings.VaultPath);
            if (fileDatabase is null) { throw new Exception("Unable to initialize file database"); }
            builder.Services.AddSingleton(fileDatabase);
            await InitializeTrackVault(fileDatabase);
        }

        private static async Task InitializeTrackVault(FileDatabase fileDatabase)
        {
            if (!fileDatabase.HasVault(VaultConstants.Tracks))
            {
                await fileDatabase.CreateVaultAsync(VaultConstants.Tracks, MediaVaultType.Audio);
            }
        }
    }
}