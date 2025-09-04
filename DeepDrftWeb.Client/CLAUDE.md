# CLAUDE.md - DeepDrftWeb.Client

This file provides guidance to Claude Code (claude.ai/code) when working with the DeepDrftWeb.Client project.

## Project Overview

DeepDrftWeb.Client is a **Blazor WebAssembly** client project that provides interactive UI components for the DeepDrft music management system. It runs in the browser and communicates with the server-side DeepDrftWeb application.

## Architecture

### Technology Stack
- **Blazor WebAssembly**: Client-side .NET runtime in browser
- **MudBlazor**: Material Design UI components  
- **HttpClient**: API communication with server
- **ASP.NET Core 9.0**: Framework components

### Project Structure
```
DeepDrftWeb.Client/
├── Pages/              # Routable page components
│   ├── Home.razor      # Home page
│   ├── Counter.razor   # Demo counter page
│   ├── Weather.razor   # Demo weather page  
│   └── TracksView.razor # Main tracks interface
├── Controls/           # Reusable UI components
│   ├── TracksGallery.razor   # Grid layout for tracks
│   └── TrackPlayer.razor     # Individual track player
├── Layout/             # Layout components
│   ├── MainLayout.razor      # Primary layout
│   └── NavMenu.razor         # Navigation menu
├── Clients/            # HTTP API clients
├── ViewModels/         # Component view models
├── wwwroot/           # Static web assets
└── Program.cs         # WebAssembly entry point
```

## Key Patterns

### MVVM Pattern
Components use ViewModels for data management and business logic separation:
```csharp
// TracksViewModel.cs - Manages tracks data and pagination
public class TracksViewModel
{
    public PagedResult<TrackEntity>? Page { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; }
    public string SortBy { get; set; }
    public bool IsDescending { get; set; }
}
```

### HTTP Client Pattern
API communication through dedicated client classes:
```csharp
// TrackClient.cs - Handles track-related API calls
public async Task<ApiResult<PagedResult<TrackEntity>>> GetPage(
    int pageNumber, int pageSize, string? sortColumn = null, bool sortDescending = false)
```

### Component Architecture
- **Pages**: Routable components (URL endpoints)
- **Controls**: Reusable components with parameters
- **Layout**: Shared layout structures

## Key Components

### TracksGallery.razor
Grid-based track display using MudBlazor responsive grid:
```razor
<MudGrid Spacing="3" Justify="Justify.Center">
    @foreach (var track in Tracks)
    {
        <MudItem xs="12" sm="6" md="4" lg="2" xl="2">
            <TrackPlayer Track="@track" />
        </MudItem>
    }
</MudGrid>
```

### TrackPlayer.razor
Individual track player component with audio controls and track information display.

### TracksView.razor
Main tracks management interface combining gallery view with pagination and sorting controls.

## Development Commands

### Building
```bash
# Build WebAssembly project
dotnet build DeepDrftWeb.Client

# Publish for production
dotnet publish DeepDrftWeb.Client -c Release
```

### Running
The client runs as part of the DeepDrftWeb host application:
```bash
# Run from DeepDrftWeb (hosts the client)
dotnet run --project DeepDrftWeb
```

## Configuration

### Service Registration
Services registered in `Startup.ConfigureDomainServices()`:
```csharp
// Clients for API communication
builder.Services.AddTransient<TrackClient>();

// ViewModels for component state management  
builder.Services.AddTransient<TracksViewModel>();
```

### HTTP Client Setup
Configured to communicate with the hosting DeepDrftWeb server:
- Uses dependency injection for HttpClient
- JSON serialization with case-insensitive property matching
- Query string building for API parameters

## Important Patterns

### API Communication
All API calls use the established result pattern:
- `ApiResult<T>` for typed responses
- JSON deserialization with `JsonSerializer`
- Query string construction for GET parameters

### MudBlazor Integration
- Responsive grid system (`MudGrid`, `MudItem`)
- Breakpoint-aware layout (xs, sm, md, lg, xl)
- Material Design components throughout

### Component Parameters
Components accept parameters for data binding:
```razor
[Parameter] public List<TrackEntity> Tracks { get; set; } = [];
[Parameter] public TrackEntity Track { get; set; } = null!;
```

### State Management
ViewModels handle component state, pagination, and sorting logic, keeping components focused on presentation.

When working with this project, maintain the separation between presentation (Razor components) and logic (ViewModels/Clients), and follow the established MudBlazor patterns for responsive UI design.