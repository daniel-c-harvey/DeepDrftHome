# CLAUDE.md - DeepDrftWeb

This file provides guidance to Claude Code (claude.ai/code) when working with the DeepDrftWeb project.

## Project Overview

DeepDrftWeb is the main web application using **Blazor Server/WebAssembly hybrid** architecture with **MudBlazor** UI framework. It serves as the front-end interface for the DeepDrft music management system.

## Architecture

### Technology Stack
- **Blazor Hybrid**: Interactive Server + WebAssembly components
- **MudBlazor**: Material Design UI framework
- **Entity Framework Core**: SQLite database access
- **ASP.NET Core 9.0**: Web framework

### Project Structure
```
DeepDrftWeb/
├── Components/           # Blazor components (.razor files)
│   ├── App.razor        # Root application component
│   ├── Pages/           # Page components
│   └── _Imports.razor   # Global using statements
├── Controllers/         # MVC API controllers
├── Data/               # Database layer
│   ├── DeepDrftContext.cs      # EF DbContext
│   ├── Configurations/         # EF configurations
│   ├── Migrations/             # EF migrations
│   └── Repositories/           # Data access layer
├── Services/           # Business logic layer
└── Program.cs          # Application entry point
```

## Key Patterns

### Service Architecture
```csharp
// Three-layer pattern: Controller → Service → Repository
TrackController → TrackService → TrackRepository → DeepDrftContext
```

### Result Pattern
All service methods return `ResultContainer<T>` or `Result` for consistent error handling:
```csharp
public async Task<ResultContainer<TrackEntity?>> GetById(long id)
{
    try
    {
        var track = await _repository.GetById(id);
        return ResultContainer<TrackEntity?>.CreatePassResult(track);
    }
    catch (Exception e)
    {
        return ResultContainer<TrackEntity?>.CreateFailResult(e.Message);
    }
}
```

### Paging Support
Uses `PagingParameters<T>` and `PagedResult<T>` from DeepDrftModels for consistent pagination:
```csharp
var parameters = new PagingParameters<TrackEntity>()
{
    Page = pageNumber,
    PageSize = pageSize,
    OrderBy = entity => entity.TrackName,
    IsDescending = sortDescending
};
```

## Development Commands

### Running the Application
```bash
# Run web application
dotnet run --project DeepDrftWeb

# Watch for changes during development
dotnet watch run --project DeepDrftWeb
```

### Entity Framework
```bash
# Add migration
dotnet ef migrations add MigrationName --project DeepDrftWeb

# Update database
dotnet ef database update --project DeepDrftWeb

# Drop database
dotnet ef database drop --project DeepDrftWeb
```

### Building
```bash
# Build project
dotnet build DeepDrftWeb

# Clean build
dotnet clean DeepDrftWeb && dotnet build DeepDrftWeb
```

## Configuration

### Database Connection
- **Connection String**: `appsettings.json` → `ConnectionStrings.DefaultConnection`
- **Database Location**: `../Database/deepdrft.db` (SQLite)
- **Context**: `DeepDrftContext` with `TrackEntity` DbSet

### Service Registration
Key services registered in `Startup.ConfigureDomainServices()`:
- `DeepDrftContext` (EF DbContext)
- `TrackRepository` (Scoped)
- `TrackService` (Scoped)

### HttpClient Setup
Configured for API communication with DeepDrftContent:
```csharp
builder.Services.AddHttpClient("DeepDrft.API", client => 
    client.BaseAddress = new Uri(Startup.GetKestrelUrl(builder)));
```

## Important Notes

### Blazor Hybrid Architecture
- Server-side rendering with interactive components
- WebAssembly components from `DeepDrftWeb.Client`
- Shared assemblies for interactive modes

### MudBlazor Integration
- UI framework providing Material Design components
- Registered via `builder.Services.AddMudServices()`

### Repository Pattern
- Clean separation of concerns
- Async operations throughout
- CRUD operations for TrackEntity

### URL Configuration
Dynamic URL resolution via `Startup.GetKestrelUrl()` supporting:
- `ASPNETCORE_URLS` environment variable
- `Kestrel:Endpoints` configuration
- Development defaults (https://localhost:5001)

When working with this project, focus on maintaining the established patterns for service layer interaction, result handling, and Blazor component structure.