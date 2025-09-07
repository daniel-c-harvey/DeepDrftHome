using Microsoft.Extensions.Logging;
using Terminal.Gui;
using DeepDrftWeb.Services.Repositories;
using DeepDrftContent.Services;
using DeepDrftModels.Entities;
using NetBlocks.Models;

namespace DeepDrftCli.Services;

/// <summary>
/// Terminal.Gui based interactive interface for DeepDrft CLI operations
/// </summary>
public class GuiService
{
    private readonly ILogger<GuiService> _logger;
    private readonly TrackRepository _trackRepository;
    private readonly DeepDrftWeb.Services.TrackService _webTrackService;
    private readonly DeepDrftContent.Services.TrackService _contentTrackService;

    // GUI Components
    private Window? _mainWindow;
    private MenuBar? _menuBar;
    private ListView? _trackListView;
    private TextView? _statusView;
    private FrameView? _legendFrame;
    private List<TrackEntity> _tracks = new();

    public GuiService(
        ILogger<GuiService> logger,
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
    /// Initialize and run the GUI application
    /// </summary>
    public async Task RunAsync()
    {
        Application.Init();
        
        try
        {
            await SetupMainWindowAsync();
            Application.Run(_mainWindow);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GUI application failed");
            MessageBox.ErrorQuery(50, 7, "Error", $"Application failed: {ex.Message}", "OK");
        }
        finally
        {
            Application.Shutdown();
        }
    }

    /// <summary>
    /// Setup the main application window with all components
    /// </summary>
    private async Task SetupMainWindowAsync()
    {
        // Create main window with DeepDrft theme
        _mainWindow = new Window("DeepDrft CLI - Interactive Mode")
        {
            X = 0,
            Y = 1, // Leave room for menu bar
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };

        // Apply DeepDrft theme to main window with improved contrast
        _mainWindow.ColorScheme = new ColorScheme()
        {
            Normal = new Terminal.Gui.Attribute(Color.White, Color.Black),
            Focus = new Terminal.Gui.Attribute(Color.White, Color.DarkGray),
            HotNormal = new Terminal.Gui.Attribute(Color.BrightCyan, Color.Black),
            HotFocus = new Terminal.Gui.Attribute(Color.BrightYellow, Color.DarkGray)
        };

        // Setup menu bar
        SetupMenuBar();

        // Setup track list view
        SetupTrackListView();

        // Setup status view
        SetupStatusView();

        // Setup legend panel
        SetupLegendPanel();

        // Setup key bindings
        SetupKeyBindings();

        // Add components to main window
        _mainWindow.Add(_trackListView!, _statusView!, _legendFrame!);

        // Load initial data
        await RefreshTrackListAsync();

        // Set initial status
        UpdateStatus("Ready - Use keyboard shortcuts shown below or press F1 for detailed help");
    }

    /// <summary>
    /// Setup the menu bar with color-coded options
    /// </summary>
    private void SetupMenuBar()
    {
        _menuBar = new MenuBar(new MenuBarItem[] {
            new MenuBarItem("_File", new MenuItem[] {
                new MenuItem("_Add Track (Ctrl+A)", "", () => ShowAddTrackDialog()),
                new MenuItem("_Refresh (F5)", "", async () => await RefreshTrackListAsync()),
                null, // Separator
                new MenuItem("_Quit (Ctrl+Q)", "", () => Application.RequestStop())
            }),
            new MenuBarItem("_Edit", new MenuItem[] {
                new MenuItem("_Edit Track (Ctrl+E)", "", () => ShowEditTrackDialog()),
                new MenuItem("_Delete Track (Delete)", "", () => ShowDeleteTrackDialog()),
                null, // Separator
                new MenuItem("_Track Details (Enter)", "", () => ShowTrackDetails()),
            }),
            new MenuBarItem("_View", new MenuItem[] {
                new MenuItem("_Clear Status (Ctrl+L)", "", () => ClearStatus()),
            }),
            new MenuBarItem("_Help", new MenuItem[] {
                new MenuItem("_Shortcuts (F1)", "", () => ShowHelp()),
                new MenuItem("_About", "", () => ShowAbout())
            })
        });

        Application.Top.Add(_menuBar);
    }

    /// <summary>
    /// Setup the track list view with color coding
    /// </summary>
    private void SetupTrackListView()
    {
        _trackListView = new ListView()
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(6), // Leave room for status and legend at bottom
            CanFocus = true
        };

        // Set up high-contrast colors for track list visibility
        _trackListView.ColorScheme = new ColorScheme()
        {
            Normal = new Terminal.Gui.Attribute(Color.White, Color.Black),
            Focus = new Terminal.Gui.Attribute(Color.BrightYellow, Color.Blue), // High contrast selection
            HotNormal = new Terminal.Gui.Attribute(Color.BrightCyan, Color.Black), 
            HotFocus = new Terminal.Gui.Attribute(Color.White, Color.Blue) // Clear cursor visibility
        };

        // Handle selection events
        _trackListView.SelectedItemChanged += OnTrackSelectionChanged;
    }

    /// <summary>
    /// Setup the status view at the bottom
    /// </summary>
    private void SetupStatusView()
    {
        _statusView = new TextView()
        {
            X = 0,
            Y = Pos.AnchorEnd(5), // Position above legend panel
            Width = Dim.Fill(),
            Height = 2,
            ReadOnly = true,
            WordWrap = true
        };

        // Status view with high contrast colors for better readability
        _statusView.ColorScheme = new ColorScheme()
        {
            Normal = new Terminal.Gui.Attribute(Color.BrightCyan, Color.Black), // High contrast status
            Focus = new Terminal.Gui.Attribute(Color.BrightCyan, Color.DarkGray)
        };
    }

    /// <summary>
    /// Setup the hotkey legend panel at the bottom
    /// </summary>
    private void SetupLegendPanel()
    {
        _legendFrame = new FrameView("Keyboard Shortcuts")
        {
            X = 0,
            Y = Pos.AnchorEnd(3),
            Width = Dim.Fill(),
            Height = 3
        };

        // Apply high contrast theme to legend frame for better visibility
        _legendFrame.ColorScheme = new ColorScheme()
        {
            Normal = new Terminal.Gui.Attribute(Color.White, Color.Black), // Clear border
            Focus = new Terminal.Gui.Attribute(Color.White, Color.DarkGray),
            HotNormal = new Terminal.Gui.Attribute(Color.BrightYellow, Color.Black),
            HotFocus = new Terminal.Gui.Attribute(Color.BrightYellow, Color.DarkGray)
        };

        // Create legend content with color coding
        var legendView = new TextView()
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            ReadOnly = true,
            WordWrap = false,
            Text = CreateLegendText()
        };

        // Legend with high contrast colors for easy reading
        legendView.ColorScheme = new ColorScheme()
        {
            Normal = new Terminal.Gui.Attribute(Color.BrightGreen, Color.Black), // High contrast legend text
            Focus = new Terminal.Gui.Attribute(Color.BrightGreen, Color.DarkGray)
        };

        _legendFrame.Add(legendView);
    }

    /// <summary>
    /// Create the formatted legend text with shortcuts
    /// </summary>
    private string CreateLegendText()
    {
        return "Ctrl+A: Add  │  Ctrl+E: Edit  │  Del: Delete  │  F5: Refresh  │  Enter: Details  │  F1: Help  │  Ctrl+Q: Quit";
    }

    /// <summary>
    /// Setup keyboard shortcuts
    /// </summary>
    private void SetupKeyBindings()
    {
        // Global key bindings using KeyDown event which is more reliable
        _mainWindow.KeyDown += async (e) =>
        {
            var key = e.KeyEvent.Key;
            
            // Debug logging for key presses
            UpdateStatus($"Key pressed: {key}");
            
            switch (key)
            {
                case Key.CtrlMask | Key.Q:
                case Key.CtrlMask | Key.q:
                    Application.RequestStop();
                    e.Handled = true;
                    break;
                    
                case Key.CtrlMask | Key.A:
                case Key.CtrlMask | Key.a:
                    UpdateStatus("Opening Add Track dialog...");
                    ShowAddTrackDialog();
                    e.Handled = true;
                    break;
                    
                case Key.CtrlMask | Key.E:
                case Key.CtrlMask | Key.e:
                    UpdateStatus("Opening Edit Track dialog...");
                    ShowEditTrackDialog();
                    e.Handled = true;
                    break;
                    
                case Key.DeleteChar:
                    UpdateStatus("Opening Delete Track dialog...");
                    ShowDeleteTrackDialog();
                    e.Handled = true;
                    break;
                    
                case Key.F5:
                    UpdateStatus("Refreshing track list...");
                    await RefreshTrackListAsync();
                    e.Handled = true;
                    break;
                    
                case Key.F1:
                    ShowHelp();
                    e.Handled = true;
                    break;
                    
                case Key.CtrlMask | Key.L:
                case Key.CtrlMask | Key.l:
                    ClearStatus();
                    e.Handled = true;
                    break;
                    
                case Key.Enter:
                    if (_trackListView?.HasFocus == true)
                    {
                        ShowTrackDetails();
                        e.Handled = true;
                    }
                    break;
            }
        };

        // Also add global application-level key bindings as backup
        Application.Top.KeyDown += async (e) =>
        {
            var key = e.KeyEvent.Key;
            
            switch (key)
            {
                case Key.CtrlMask | Key.A:
                case Key.CtrlMask | Key.a:
                    if (!e.Handled)
                    {
                        UpdateStatus("Global: Opening Add Track dialog...");
                        ShowAddTrackDialog();
                        e.Handled = true;
                    }
                    break;
            }
        };
    }

    /// <summary>
    /// Show the Add Track dialog with form validation
    /// </summary>
    private void ShowAddTrackDialog()
    {
        var dialog = new Dialog("Add New Track", 80, 18);
        
        // File path field
        var filePathLabel = new Label("WAV File Path:") { X = 1, Y = 1 };
        var filePathField = new TextField("") { X = 1, Y = 2, Width = Dim.Fill(2) };
        var browseButton = new Button("Browse...") { X = Pos.AnchorEnd(12), Y = 2 };
        
        // Track metadata fields
        var trackNameLabel = new Label("Track Name: *") { X = 1, Y = 4 };
        var trackNameField = new TextField("") { X = 1, Y = 5, Width = Dim.Fill(2) };
        
        var artistLabel = new Label("Artist: *") { X = 1, Y = 6 };
        var artistField = new TextField("") { X = 1, Y = 7, Width = Dim.Fill(2) };
        
        var albumLabel = new Label("Album:") { X = 1, Y = 8 };
        var albumField = new TextField("") { X = 1, Y = 9, Width = Dim.Fill(2) };
        
        var genreLabel = new Label("Genre:") { X = 1, Y = 10 };
        var genreField = new TextField("") { X = 1, Y = 11, Width = Dim.Fill(2) };
        
        var releaseDateLabel = new Label("Release Date (YYYY-MM-DD):") { X = 1, Y = 12 };
        var releaseDateField = new TextField("") { X = 1, Y = 13, Width = Dim.Fill(2) };

        // Buttons
        var addButton = new Button("Add Track") { X = 1, Y = 15 };
        var cancelButton = new Button("Cancel") { X = 15, Y = 15 };

        // Color coding for required fields with high contrast
        trackNameLabel.ColorScheme = new ColorScheme()
        {
            Normal = new Terminal.Gui.Attribute(Color.BrightRed, Color.Black) // High contrast for required fields
        };
        artistLabel.ColorScheme = new ColorScheme()
        {
            Normal = new Terminal.Gui.Attribute(Color.BrightRed, Color.Black) // High contrast for required fields
        };

        // Browse button click handler
        browseButton.Clicked += () =>
        {
            var openDialog = new OpenDialog("Select WAV File", "Select a WAV audio file to add");
            openDialog.AllowedFileTypes = new[] { ".wav" };
            
            Application.Run(openDialog);
            
            if (!openDialog.Canceled && openDialog.FilePath != null)
            {
                filePathField.Text = openDialog.FilePath.ToString();
                
                // Try to extract metadata from filename
                var fileName = Path.GetFileNameWithoutExtension(openDialog.FilePath.ToString());
                if (string.IsNullOrEmpty(trackNameField.Text.ToString()))
                {
                    trackNameField.Text = fileName;
                }
            }
        };

        // Add button click handler
        addButton.Clicked += async () =>
        {
            if (await ValidateAndAddTrackAsync(
                filePathField.Text.ToString(),
                trackNameField.Text.ToString(),
                artistField.Text.ToString(),
                albumField.Text.ToString(),
                genreField.Text.ToString(),
                releaseDateField.Text.ToString()))
            {
                Application.RequestStop();
            }
        };

        // Cancel button click handler
        cancelButton.Clicked += () => Application.RequestStop();

        // Add all components to dialog
        dialog.Add(filePathLabel, filePathField, browseButton,
                  trackNameLabel, trackNameField,
                  artistLabel, artistField,
                  albumLabel, albumField,
                  genreLabel, genreField,
                  releaseDateLabel, releaseDateField,
                  addButton, cancelButton);

        // Set focus to file path field
        filePathField.SetFocus();

        Application.Run(dialog);
    }

    /// <summary>
    /// Show the Edit Track dialog for the selected track
    /// </summary>
    private void ShowEditTrackDialog()
    {
        if (_trackListView?.SelectedItem < 0 || _trackListView?.SelectedItem >= _tracks.Count)
        {
            UpdateStatus("No track selected for editing. Select a track first.");
            return;
        }

        var selectedTrack = _tracks[_trackListView!.SelectedItem];
        
        var dialog = new Dialog("Edit Track", 80, 18);
        
        // Track metadata fields pre-filled with current values
        var trackNameLabel = new Label("Track Name: *") { X = 1, Y = 1 };
        var trackNameField = new TextField(selectedTrack.TrackName) { X = 1, Y = 2, Width = Dim.Fill(2) };
        
        var artistLabel = new Label("Artist: *") { X = 1, Y = 3 };
        var artistField = new TextField(selectedTrack.Artist) { X = 1, Y = 4, Width = Dim.Fill(2) };
        
        var albumLabel = new Label("Album:") { X = 1, Y = 5 };
        var albumField = new TextField(selectedTrack.Album ?? "") { X = 1, Y = 6, Width = Dim.Fill(2) };
        
        var genreLabel = new Label("Genre:") { X = 1, Y = 7 };
        var genreField = new TextField(selectedTrack.Genre ?? "") { X = 1, Y = 8, Width = Dim.Fill(2) };
        
        var releaseDateLabel = new Label("Release Date (YYYY-MM-DD):") { X = 1, Y = 9 };
        var releaseDateField = new TextField(selectedTrack.ReleaseDate?.ToString() ?? "") { X = 1, Y = 10, Width = Dim.Fill(2) };

        // Info label showing current track ID
        var infoLabel = new Label($"Editing Track ID: {selectedTrack.Id} - Entry Key: {selectedTrack.EntryKey}") { X = 1, Y = 12 };

        // Buttons
        var saveButton = new Button("Save Changes") { X = 1, Y = 14 };
        var cancelButton = new Button("Cancel") { X = 18, Y = 14 };

        // Color coding for required fields with high contrast
        trackNameLabel.ColorScheme = new ColorScheme()
        {
            Normal = new Terminal.Gui.Attribute(Color.BrightRed, Color.Black)
        };
        artistLabel.ColorScheme = new ColorScheme()
        {
            Normal = new Terminal.Gui.Attribute(Color.BrightRed, Color.Black)
        };
        
        // Info label styling with better contrast
        infoLabel.ColorScheme = new ColorScheme()
        {
            Normal = new Terminal.Gui.Attribute(Color.BrightCyan, Color.Black)
        };

        // Save button click handler
        saveButton.Clicked += async () =>
        {
            if (await ValidateAndUpdateTrackAsync(selectedTrack, 
                trackNameField.Text.ToString(),
                artistField.Text.ToString(),
                albumField.Text.ToString(),
                genreField.Text.ToString(),
                releaseDateField.Text.ToString()))
            {
                Application.RequestStop();
            }
        };

        // Cancel button click handler
        cancelButton.Clicked += () => Application.RequestStop();

        // Add all components to dialog
        dialog.Add(trackNameLabel, trackNameField,
                  artistLabel, artistField,
                  albumLabel, albumField,
                  genreLabel, genreField,
                  releaseDateLabel, releaseDateField,
                  infoLabel,
                  saveButton, cancelButton);

        // Set focus to track name field
        trackNameField.SetFocus();

        Application.Run(dialog);
    }

    /// <summary>
    /// Show the Delete Track confirmation dialog for the selected track
    /// </summary>
    private void ShowDeleteTrackDialog()
    {
        if (_trackListView?.SelectedItem < 0 || _trackListView?.SelectedItem >= _tracks.Count)
        {
            UpdateStatus("No track selected for deletion. Select a track first.");
            return;
        }

        var selectedTrack = _tracks[_trackListView!.SelectedItem];
        
        var message = $"Are you sure you want to delete this track?\n\n" +
                     $"Track: {selectedTrack.TrackName}\n" +
                     $"Artist: {selectedTrack.Artist}\n" +
                     $"Album: {selectedTrack.Album ?? "N/A"}\n" +
                     $"Genre: {selectedTrack.Genre ?? "N/A"}\n" +
                     $"ID: {selectedTrack.Id}\n\n" +
                     $"WARNING: This action cannot be undone!\n" +
                     $"The track metadata will be removed from the database.";

        var result = MessageBox.Query(70, 14, "Confirm Delete Track", message, "Delete", "Cancel");
        
        if (result == 0) // Delete button clicked
        {
            _ = Task.Run(async () =>
            {
                await DeleteTrackAsync(selectedTrack);
            });
        }
    }

    /// <summary>
    /// Delete the specified track from the database
    /// </summary>
    private async Task DeleteTrackAsync(TrackEntity trackToDelete)
    {
        try
        {
            UpdateStatus($"Deleting track '{trackToDelete.TrackName}'...");

            // Delete from SQL database
            var result = await _webTrackService.Delete(trackToDelete.Id);
            if (result.Success)
            {
                UpdateStatus($"✓ Track '{trackToDelete.TrackName}' by {trackToDelete.Artist} deleted successfully!");
                await RefreshTrackListAsync();
            }
            else
            {
                var errorMessage = result.Messages.FirstOrDefault()?.Message ?? "Unknown error";
                UpdateStatus($"Failed to delete track: {errorMessage}");
                
                // Show error dialog on UI thread
                Application.MainLoop.Invoke(() =>
                {
                    MessageBox.ErrorQuery(60, 8, "Database Error", $"Failed to delete track: {errorMessage}", "OK");
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete track via GUI");
            UpdateStatus($"Error deleting track: {ex.Message}");
            
            // Show error dialog on UI thread
            Application.MainLoop.Invoke(() =>
            {
                MessageBox.ErrorQuery(60, 8, "Error", $"An error occurred: {ex.Message}", "OK");
            });
        }
    }

    /// <summary>
    /// Validate input and add track to database
    /// </summary>
    private async Task<bool> ValidateAndAddTrackAsync(string filePath, string trackName, 
        string artist, string album, string genre, string releaseDate)
    {
        try
        {
            // Validate required fields
            if (string.IsNullOrWhiteSpace(filePath))
            {
                MessageBox.ErrorQuery(50, 7, "Validation Error", "File path is required.", "OK");
                return false;
            }

            if (string.IsNullOrWhiteSpace(trackName))
            {
                MessageBox.ErrorQuery(50, 7, "Validation Error", "Track name is required.", "OK");
                return false;
            }

            if (string.IsNullOrWhiteSpace(artist))
            {
                MessageBox.ErrorQuery(50, 7, "Validation Error", "Artist is required.", "OK");
                return false;
            }

            // Validate file exists and has .wav extension
            if (!File.Exists(filePath))
            {
                MessageBox.ErrorQuery(50, 7, "File Error", "The specified file does not exist.", "OK");
                return false;
            }

            if (!Path.GetExtension(filePath).Equals(".wav", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.ErrorQuery(50, 7, "File Error", "Only WAV files are supported.", "OK");
                return false;
            }

            // Validate release date if provided
            DateOnly? parsedReleaseDate = null;
            if (!string.IsNullOrWhiteSpace(releaseDate))
            {
                if (!DateOnly.TryParse(releaseDate, out var date))
                {
                    MessageBox.ErrorQuery(50, 7, "Date Error", "Release date must be in YYYY-MM-DD format.", "OK");
                    return false;
                }
                parsedReleaseDate = date;
            }

            // Show progress dialog
            UpdateStatus("Processing audio file...");

            // Initialize tracks vault
            await _contentTrackService.InitializeTracksVaultAsync();

            // Process and add track
            var trackEntity = await _contentTrackService.AddTrackFromWavAsync(
                filePath, trackName, artist, 
                string.IsNullOrWhiteSpace(album) ? null : album,
                string.IsNullOrWhiteSpace(genre) ? null : genre,
                parsedReleaseDate);

            if (trackEntity == null)
            {
                MessageBox.ErrorQuery(50, 7, "Processing Error", "Failed to process audio file.", "OK");
                return false;
            }

            // Add to SQL database
            var result = await _webTrackService.Create(trackEntity);
            if (result.Success && result.Value != null)
            {
                UpdateStatus($"✓ Track '{trackName}' by {artist} added successfully!");
                await RefreshTrackListAsync();
                return true;
            }
            else
            {
                var errorMessage = result.Messages.FirstOrDefault()?.Message ?? "Unknown error";
                MessageBox.ErrorQuery(60, 8, "Database Error", $"Failed to save track: {errorMessage}", "OK");
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add track via GUI");
            MessageBox.ErrorQuery(60, 8, "Error", $"An error occurred: {ex.Message}", "OK");
            return false;
        }
    }

    /// <summary>
    /// Validate input and update existing track in database
    /// </summary>
    private async Task<bool> ValidateAndUpdateTrackAsync(TrackEntity originalTrack, string trackName, 
        string artist, string album, string genre, string releaseDate)
    {
        try
        {
            // Validate required fields
            if (string.IsNullOrWhiteSpace(trackName))
            {
                MessageBox.ErrorQuery(50, 7, "Validation Error", "Track name is required.", "OK");
                return false;
            }

            if (string.IsNullOrWhiteSpace(artist))
            {
                MessageBox.ErrorQuery(50, 7, "Validation Error", "Artist is required.", "OK");
                return false;
            }

            // Validate release date if provided
            DateOnly? parsedReleaseDate = null;
            if (!string.IsNullOrWhiteSpace(releaseDate))
            {
                if (!DateOnly.TryParse(releaseDate, out var date))
                {
                    MessageBox.ErrorQuery(50, 7, "Date Error", "Release date must be in YYYY-MM-DD format.", "OK");
                    return false;
                }
                parsedReleaseDate = date;
            }

            UpdateStatus("Updating track...");

            // Create updated track entity
            var updatedTrack = new TrackEntity
            {
                Id = originalTrack.Id,
                EntryKey = originalTrack.EntryKey, // Keep original entry key
                TrackName = trackName,
                Artist = artist,
                Album = string.IsNullOrWhiteSpace(album) ? null : album,
                Genre = string.IsNullOrWhiteSpace(genre) ? null : genre,
                ReleaseDate = parsedReleaseDate,
                ImagePath = originalTrack.ImagePath // Keep original image path
            };

            // Update in SQL database
            var result = await _webTrackService.Update(updatedTrack);
            if (result.Success && result.Value != null)
            {
                UpdateStatus($"✓ Track '{trackName}' by {artist} updated successfully!");
                await RefreshTrackListAsync();
                return true;
            }
            else
            {
                var errorMessage = result.Messages.FirstOrDefault()?.Message ?? "Unknown error";
                MessageBox.ErrorQuery(60, 8, "Database Error", $"Failed to update track: {errorMessage}", "OK");
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update track via GUI");
            MessageBox.ErrorQuery(60, 8, "Error", $"An error occurred: {ex.Message}", "OK");
            return false;
        }
    }

    /// <summary>
    /// Show detailed information about the selected track
    /// </summary>
    private void ShowTrackDetails()
    {
        if (_trackListView?.SelectedItem < 0 || _trackListView?.SelectedItem >= _tracks.Count)
        {
            UpdateStatus("No track selected.");
            return;
        }

        var selectedTrack = _tracks[_trackListView!.SelectedItem];
        
        var details = $"Track Details:\n\n" +
                     $"ID: {selectedTrack.Id}\n" +
                     $"Name: {selectedTrack.TrackName}\n" +
                     $"Artist: {selectedTrack.Artist}\n" +
                     $"Album: {selectedTrack.Album ?? "N/A"}\n" +
                     $"Genre: {selectedTrack.Genre ?? "N/A"}\n" +
                     $"Release Date: {selectedTrack.ReleaseDate?.ToString() ?? "N/A"}\n" +
                     $"Entry Key: {selectedTrack.EntryKey}\n" +
                     $"Image Path: {selectedTrack.ImagePath ?? "N/A"}";

        MessageBox.Query(70, 12, "Track Details", details, "OK");
    }

    /// <summary>
    /// Refresh the track list from database
    /// </summary>
    private async Task RefreshTrackListAsync()
    {
        try
        {
            UpdateStatus("Loading tracks...");
            
            var result = await _webTrackService.GetAll();
            if (result.Success && result.Value != null)
            {
                _tracks = result.Value.ToList();
                
                // Create display items for the list view
                var displayItems = _tracks.Select(t => 
                    $"{t.Id,4} │ {TruncateString(t.TrackName, 25),25} │ {TruncateString(t.Artist, 20),20} │ {TruncateString(t.Album ?? "", 15),15} │ {TruncateString(t.Genre ?? "", 10),10}"
                ).ToArray();

                _trackListView?.SetSource(displayItems);
                
                UpdateStatus($"Loaded {_tracks.Count} tracks. Use shortcuts below or navigate with ↑/↓ arrows.");
            }
            else
            {
                var errorMessage = result.Messages.FirstOrDefault()?.Message ?? "Unknown error";
                UpdateStatus($"Failed to load tracks: {errorMessage}");
                MessageBox.ErrorQuery(50, 7, "Database Error", $"Failed to load tracks: {errorMessage}", "OK");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh track list");
            UpdateStatus($"Error loading tracks: {ex.Message}");
        }
    }

    /// <summary>
    /// Handle track selection changes
    /// </summary>
    private void OnTrackSelectionChanged(ListViewItemEventArgs args)
    {
        if (args.Item >= 0 && args.Item < _tracks.Count)
        {
            var selectedTrack = _tracks[args.Item];
            UpdateStatus($"Selected: {selectedTrack.TrackName} by {selectedTrack.Artist} - Press Enter for full details");
        }
    }

    /// <summary>
    /// Show help dialog with keyboard shortcuts
    /// </summary>
    private void ShowHelp()
    {
        var helpText = 
            "DeepDrft CLI - Interactive Mode Help\n\n" +
            "KEYBOARD SHORTCUTS (also shown in legend at bottom):\n" +
            "Ctrl+A     - Add new track\n" +
            "Ctrl+E     - Edit selected track\n" +
            "Delete     - Delete selected track\n" +
            "F5         - Refresh track list\n" +
            "Enter      - Show track details\n" +
            "Ctrl+L     - Clear status\n" +
            "F1         - Show this help\n" +
            "Ctrl+Q     - Quit application\n\n" +
            "NAVIGATION:\n" +
            "↑/↓        - Navigate track list\n" +
            "Tab        - Switch between controls\n" +
            "Space      - Select/activate control\n\n" +
            "USER INTERFACE:\n" +
            "• Track list shows: ID │ Name │ Artist │ Album │ Genre\n" +
            "• Status bar provides real-time feedback\n" +
            "• Legend bar shows common shortcuts\n" +
            "• Menu bar accessible via Alt or mouse\n\n" +
            "HIGH CONTRAST COLOR SCHEME:\n" +
            "Bright Red      - Required fields (*)\n" +
            "Bright Yellow   - Selected/focused items\n" +
            "Blue Background - Selection highlight\n" +
            "Bright Cyan     - Status messages & info\n" +
            "Bright Green    - Legend shortcuts\n" +
            "Bright White    - Normal text & borders";

        MessageBox.Query(70, 22, "Help - Interactive Mode Guide", helpText, "OK");
    }

    /// <summary>
    /// Show about dialog
    /// </summary>
    private void ShowAbout()
    {
        var aboutText = 
            "DeepDrft CLI - Interactive Mode\n\n" +
            "Version: 1.0.0\n" +
            "Built with Terminal.Gui\n\n" +
            "Features:\n" +
            "• Interactive track management\n" +
            "• Dual database architecture\n" +
            "• WAV file processing\n" +
            "• Color-coded interface\n" +
            "• Keyboard shortcuts\n\n" +
            "© 2025 DeepDrft Project";

        MessageBox.Query(50, 12, "About DeepDrft CLI", aboutText, "OK");
    }

    /// <summary>
    /// Update the status display
    /// </summary>
    private void UpdateStatus(string message)
    {
        if (_statusView != null)
        {
            _statusView.Text = $"Status: {message}";
            _statusView.SetNeedsDisplay();
        }
    }

    /// <summary>
    /// Clear the status display
    /// </summary>
    private void ClearStatus()
    {
        UpdateStatus("Ready");
    }

    /// <summary>
    /// Truncate string to fit display width
    /// </summary>
    private string TruncateString(string input, int maxLength)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;
            
        return input.Length <= maxLength ? input : input.Substring(0, maxLength - 3) + "...";
    }
}