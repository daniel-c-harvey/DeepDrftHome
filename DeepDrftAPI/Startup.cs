using DeepDrftAPI.Models;
using DeepDrftContent;
using DeepDrftContent.Audio;
using DeepDrftContent.Constants;
using DeepDrftContent.FileDatabase.Models;
using DeepDrftContent.FileDatabase.Services;
using DeepDrftContent.Processors;
using Microsoft.Extensions.Logging;
using NetBlocks.Utilities.Environment;

namespace DeepDrftAPI
{
    public static class Startup
    {
        public static Task ConfigureDomainServices(WebApplicationBuilder builder)
        {
            // Audio services
            builder.Services.AddSingleton<WavOffsetService>();
            builder.Services.AddSingleton<AudioProcessor>();
            builder.Services.AddSingleton<TrackContentService>();

            // File Database
            var fileDatabasePath = CredentialTools.ResolvePathOrThrow("filedatabase", "environment/filedatabase.json");
            builder.Configuration.AddJsonFile(fileDatabasePath, optional: false, reloadOnChange: false);
            var fileDatabaseSettings = builder.Configuration.GetSection(nameof(FileDatabaseSettings)).Get<FileDatabaseSettings>();
            if (fileDatabaseSettings is null) { throw new Exception("File database settings are not configured"); }

            var vaultPath = fileDatabaseSettings.VaultPath;
            builder.Services.AddSingleton(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<FileDatabase>>();
                var db = FileDatabase.FromAsync(vaultPath, logger).GetAwaiter().GetResult();
                if (db is null) throw new Exception("Unable to initialize file database");
                InitializeTrackVault(db).GetAwaiter().GetResult();
                return db;
            });

            return Task.CompletedTask;
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