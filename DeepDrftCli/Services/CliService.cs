using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using DeepDrftWeb.Services.Repositories;
using DeepDrftContent.Services;
using DeepDrftModels.Entities;
using NetBlocks.Models;

namespace DeepDrftCli.Services;

/// <summary>
/// Main CLI service for handling command-line operations
/// </summary>
public class CliService
{
    private readonly ILogger<CliService> _logger;
    private readonly TrackRepository _trackRepository;
    private readonly DeepDrftWeb.Services.TrackService _webTrackService;
    private readonly DeepDrftContent.Services.TrackService _contentTrackService;

    public CliService(
        ILogger<CliService> logger,
        TrackRepository trackRepository,
        DeepDrftWeb.Services.TrackService webTrackService,
        DeepDrftContent.Services.TrackService contentTrackService)
    {
        _logger = logger;
        _trackRepository = trackRepository;
        _webTrackService = webTrackService;
        _contentTrackService = contentTrackService;
    }

    /// <summary>
    /// Main entry point for CLI operations
    /// </summary>
    public async Task RunAsync(string[] args)
    {
        try
        {
            if (args.Length == 0)
            {
                ShowHelp();
                return;
            }

            var command = args[0].ToLowerInvariant();
            switch (command)
            {
                case "add":
                    await HandleAddCommand(args);
                    break;
                case "list":
                    await HandleListCommand();
                    break;
                case "gui":
                case "--gui":
                    Console.WriteLine("Error: GUI mode should be launched directly. Use: DeepDrftCli gui");
                    break;
                case "help":
                case "--help":
                case "-h":
                    ShowHelp();
                    break;
                default:
                    Console.WriteLine($"Unknown command: {command}");
                    ShowHelp();
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CLI operation failed");
            Console.WriteLine($"Error: {ex.Message}");
            Environment.Exit(1);
        }
    }

    /// <summary>
    /// Handles the add command to add a new track
    /// </summary>
    private async Task HandleAddCommand(string[] args)
    {
        // Check if we have at least the command and file path
        if (args.Length < 2)
        {
            Console.WriteLine("Error: WAV file path is required.");
            Console.WriteLine();
            Console.WriteLine("Usage: DeepDrftCli add <wav-file-path> [-i|--interactive] [track-name] [artist] [album] [genre] [release-date]");
            Console.WriteLine("       DeepDrftCli add <wav-file-path> -i  (interactive mode)");
            Console.WriteLine("Example: DeepDrftCli add \"song.wav\" \"My Song\" \"Artist Name\" \"Album Name\" \"Rock\" \"2024-01-01\"");
            Console.WriteLine("Example: DeepDrftCli add \"song.wav\" --interactive");
            return;
        }

        var wavFilePath = args[1];
        
        // Validate that the file path is not a flag
        if (wavFilePath.StartsWith("-"))
        {
            Console.WriteLine("Error: WAV file path is required and cannot be a flag.");
            Console.WriteLine();
            Console.WriteLine("Usage: DeepDrftCli add <wav-file-path> [-i|--interactive] [track-name] [artist] [album] [genre] [release-date]");
            Console.WriteLine("       DeepDrftCli add <wav-file-path> -i  (interactive mode)");
            Console.WriteLine("Example: DeepDrftCli add \"song.wav\" \"My Song\" \"Artist Name\" \"Album Name\" \"Rock\" \"2024-01-01\"");
            Console.WriteLine("Example: DeepDrftCli add \"song.wav\" --interactive");
            return;
        }

        var isInteractive = args.Contains("-i") || args.Contains("--interactive");
        
        // Filter out the interactive flags from args for processing
        var filteredArgs = args.Where(arg => arg != "-i" && arg != "--interactive").ToArray();
        
        string trackName;
        string artist;
        string? album;
        string? genre;
        DateOnly? releaseDate = null;

        if (isInteractive)
        {
            // Interactive mode - prompt for metadata
            var metadata = PromptForMetadata(wavFilePath, filteredArgs);
            trackName = metadata.TrackName;
            artist = metadata.Artist;
            album = metadata.Album;
            genre = metadata.Genre;
            releaseDate = metadata.ReleaseDate;
        }
        else
        {
            // Traditional command-line mode
            if (filteredArgs.Length < 4)
            {
                Console.WriteLine("Usage: DeepDrftCli add <wav-file-path> <track-name> <artist> [album] [genre] [release-date]");
                Console.WriteLine("       DeepDrftCli add <wav-file-path> -i  (interactive mode)");
                Console.WriteLine("Example: DeepDrftCli add \"song.wav\" \"My Song\" \"Artist Name\" \"Album Name\" \"Rock\" \"2024-01-01\"");
                Console.WriteLine("Example: DeepDrftCli add \"song.wav\" --interactive");
                return;
            }

            trackName = filteredArgs[2];
            artist = filteredArgs[3];
            album = filteredArgs.Length > 4 ? filteredArgs[4] : null;
            genre = filteredArgs.Length > 5 ? filteredArgs[5] : null;

            if (filteredArgs.Length > 6 && DateOnly.TryParse(filteredArgs[6], out var parsedDate))
            {
                releaseDate = parsedDate;
            }
        }

        Console.WriteLine($"Adding track: {trackName} by {artist}");
        Console.WriteLine($"Processing WAV file: {wavFilePath}");

        try
        {
            // Initialize tracks vault if needed
            await _contentTrackService.InitializeTracksVaultAsync();

            // Add track to FileDatabase and get entity
            var trackEntity = await _contentTrackService.AddTrackFromWavAsync(
                wavFilePath, trackName, artist, album, genre, releaseDate);

            if (trackEntity == null)
            {
                Console.WriteLine("Failed to process audio file");
                return;
            }

            // Add track to SQL database
            var result = await _webTrackService.Create(trackEntity);
            if (result.Success && result.Value != null)
            {
                Console.WriteLine($"✓ Track added successfully!");
                Console.WriteLine($"  ID: {result.Value.Id}");
                Console.WriteLine($"  Name: {result.Value.TrackName}");
                Console.WriteLine($"  Artist: {result.Value.Artist}");
                Console.WriteLine($"  Album: {result.Value.Album ?? "N/A"}");
                Console.WriteLine($"  Genre: {result.Value.Genre ?? "N/A"}");
                Console.WriteLine($"  Release Date: {result.Value.ReleaseDate?.ToString() ?? "N/A"}");
                Console.WriteLine($"  Entry Key: {result.Value.EntryKey}");
            }
            else
            {
                var errorMessage = result.Messages.FirstOrDefault()?.Message ?? "Unknown error";
                Console.WriteLine($"Failed to save track to database: {errorMessage}");
            }
        }
        catch (FileNotFoundException)
        {
            Console.WriteLine($"Error: WAV file not found: {wavFilePath}");
        }
        catch (ArgumentException ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error adding track: {ex.Message}");
            _logger.LogError(ex, "Failed to add track");
        }
    }

    /// <summary>
    /// Handles the list command to show all tracks
    /// </summary>
    private async Task HandleListCommand()
    {
        try
        {
            Console.WriteLine("Retrieving tracks from database...");
            
            var result = await _webTrackService.GetAll();
            if (!result.Success || result.Value == null)
            {
                var errorMessage = result.Messages.FirstOrDefault()?.Message ?? "Unknown error";
                Console.WriteLine($"Failed to retrieve tracks: {errorMessage}");
                return;
            }

            var tracks = result.Value;
            if (tracks.Count == 0)
            {
                Console.WriteLine("No tracks found in database.");
                return;
            }

            Console.WriteLine($"\nFound {tracks.Count} tracks:");
            Console.WriteLine(new string('-', 80));
            Console.WriteLine($"{"ID",-5} {"Name",-25} {"Artist",-20} {"Album",-15} {"Genre",-10}");
            Console.WriteLine(new string('-', 80));

            foreach (var track in tracks)
            {
                Console.WriteLine($"{track.Id,-5} {TruncateString(track.TrackName, 25),-25} {TruncateString(track.Artist, 20),-20} {TruncateString(track.Album ?? "", 15),-15} {TruncateString(track.Genre ?? "", 10),-10}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error listing tracks: {ex.Message}");
            _logger.LogError(ex, "Failed to list tracks");
        }
    }

    /// <summary>
    /// Shows help information
    /// </summary>
    private void ShowHelp()
    {
        Console.WriteLine("DeepDrft CLI - Audio Track Management Tool");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  DeepDrftCli gui                    - Launch interactive GUI mode");
        Console.WriteLine("  DeepDrftCli [command] [options]    - Run command-line mode");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  add <wav-file> <track-name> <artist> [album] [genre] [release-date]");
        Console.WriteLine("    - Adds a WAV file to both SQL and FileDatabase");
        Console.WriteLine("    - Example: DeepDrftCli add \"song.wav\" \"My Song\" \"Artist\" \"Album\" \"Rock\" \"2024-01-01\"");
        Console.WriteLine();
        Console.WriteLine("  add <wav-file> -i|--interactive [track-name] [artist] [album] [genre] [release-date]");
        Console.WriteLine("    - Adds a WAV file with interactive metadata prompts");
        Console.WriteLine("    - Any provided command-line arguments will be used as defaults");
        Console.WriteLine("    - Example: DeepDrftCli add \"song.wav\" -i");
        Console.WriteLine("    - Example: DeepDrftCli add \"song.wav\" --interactive \"My Song\"");
        Console.WriteLine();
        Console.WriteLine("  list");
        Console.WriteLine("    - Lists all tracks in the database");
        Console.WriteLine();
        Console.WriteLine("  help");
        Console.WriteLine("    - Shows this help information");
        Console.WriteLine();
        Console.WriteLine("Interactive Mode Features:");
        Console.WriteLine("  - Prompts for each metadata field individually");
        Console.WriteLine("  - Shows file name being processed");
        Console.WriteLine("  - Supports default values and fallback to command-line args");
        Console.WriteLine("  - Required fields: Track Name, Artist");
        Console.WriteLine("  - Optional fields: Album, Genre, Release Date");
        Console.WriteLine("  - Summary confirmation before proceeding");
        Console.WriteLine();
        Console.WriteLine("Notes:");
        Console.WriteLine("  - Only WAV files are supported");
        Console.WriteLine("  - Release date format: YYYY-MM-DD");
        Console.WriteLine("  - Arguments with spaces should be quoted");
        Console.WriteLine("  - Use * to indicate required fields in interactive mode");
    }

    /// <summary>
    /// Truncates a string to fit within specified length
    /// </summary>
    private string TruncateString(string input, int maxLength)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;
            
        return input.Length <= maxLength ? input : input.Substring(0, maxLength - 3) + "...";
    }

    /// <summary>
    /// Prompts user for track metadata interactively
    /// </summary>
    private TrackMetadata PromptForMetadata(string wavFilePath, string[] args)
    {
        Console.WriteLine();
        Console.WriteLine("=== Interactive Metadata Entry ===");
        Console.WriteLine($"Processing file: {Path.GetFileName(wavFilePath)}");
        Console.WriteLine("Press Enter to use default values or skip optional fields.");
        Console.WriteLine();

        // Check if any metadata was provided via command line (fallback support)
        var trackName = args.Length > 2 ? args[2] : null;
        var artist = args.Length > 3 ? args[3] : null;
        var album = args.Length > 4 ? args[4] : null;
        var genre = args.Length > 5 ? args[5] : null;
        DateOnly? releaseDate = null;
        if (args.Length > 6 && DateOnly.TryParse(args[6], out var parsedDate))
            releaseDate = parsedDate;

        // Prompt for track name (required)
        trackName ??= PromptForInput("Track Name", required: true);

        // Prompt for artist (required)
        artist ??= PromptForInput("Artist", required: true);

        // Prompt for album (optional)
        album ??= PromptForInput("Album", defaultValue: album);

        // Prompt for genre (optional)
        genre ??= PromptForInput("Genre", defaultValue: genre);

        // Prompt for release date (optional)
        if (releaseDate == null)
        {
            var releaseDateInput = PromptForInput("Release Date (YYYY-MM-DD)", defaultValue: releaseDate?.ToString());
            if (!string.IsNullOrWhiteSpace(releaseDateInput) && DateOnly.TryParse(releaseDateInput, out var newReleaseDate))
            {
                releaseDate = newReleaseDate;
            }
        }

        Console.WriteLine();
        Console.WriteLine("=== Summary ===");
        Console.WriteLine($"Track Name: {trackName}");
        Console.WriteLine($"Artist: {artist}");
        Console.WriteLine($"Album: {album ?? "N/A"}");
        Console.WriteLine($"Genre: {genre ?? "N/A"}");
        Console.WriteLine($"Release Date: {releaseDate?.ToString() ?? "N/A"}");
        Console.WriteLine();

        if (!ConfirmProceed("Proceed with these details?"))
        {
            Console.WriteLine("Operation cancelled.");
            Environment.Exit(0);
        }

        return new TrackMetadata
        {
            TrackName = trackName,
            Artist = artist,
            Album = album,
            Genre = genre,
            ReleaseDate = releaseDate
        };
    }

    /// <summary>
    /// Prompts user for a single input field
    /// </summary>
    private string PromptForInput(string fieldName, bool required = false, string? defaultValue = null)
    {
        while (true)
        {
            var prompt = $"{fieldName}";
            if (!string.IsNullOrWhiteSpace(defaultValue))
                prompt += $" [{defaultValue}]";
            if (required)
                prompt += " *";
            prompt += ": ";

            Console.Write(prompt);
            var input = Console.ReadLine();

            // Handle null input (Ctrl+C scenario)
            if (input == null)
            {
                Console.WriteLine();
                Console.WriteLine("Operation cancelled.");
                Environment.Exit(0);
            }

            input = input.Trim();

            // Use default value if input is empty and default exists
            if (string.IsNullOrWhiteSpace(input) && !string.IsNullOrWhiteSpace(defaultValue))
                return defaultValue;

            // If not required and input is empty, return empty string
            if (!required && string.IsNullOrWhiteSpace(input))
                return string.Empty;

            // Check if required field is provided
            if (required && string.IsNullOrWhiteSpace(input))
            {
                Console.WriteLine($"Error: {fieldName} is required. Please provide a value.");
                continue;
            }

            return input;
        }
    }

    /// <summary>
    /// Prompts user for yes/no confirmation
    /// </summary>
    private bool ConfirmProceed(string message)
    {
        while (true)
        {
            Console.Write($"{message} (y/n): ");
            var input = Console.ReadKey();
            Console.WriteLine();

            switch (input.KeyChar.ToString().ToLowerInvariant())
            {
                case "y":
                    return true;
                case "n":
                    return false;
                default:
                    Console.WriteLine("Please enter 'y' for yes or 'n' for no.");
                    break;
            }
        }
    }

    /// <summary>
    /// Represents track metadata for interactive input
    /// </summary>
    private record TrackMetadata
    {
        public required string TrackName { get; init; }
        public required string Artist { get; init; }
        public string? Album { get; init; }
        public string? Genre { get; init; }
        public DateOnly? ReleaseDate { get; init; }
    }
}