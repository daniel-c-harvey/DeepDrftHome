# CLAUDE.md - DeepDrftData

Guidance for working in the DeepDrftData project (the SQL-side domain logic).

See the root `CLAUDE.md` for full architecture overview. This file covers what is specific to this project.

## One-line purpose

SQL-side domain logic for tracks. EF Core context, configurations, migrations, repository, manager, design-time factory. Consumed by `DeepDrftContent` (the dual-database authority), `DeepDrftPublic` (the public API), and `DeepDrftCli` (the admin CLI).

## Why this project exists

Separating domain logic from hosts so multiple consumers (CLI, public API, content API) can reuse `TrackManager` / `TrackRepository` / `DeepDrftContext` without referencing the ASP.NET hosts directly. The CLI is a local admin tool; the APIs proxy these services over HTTP.

**New SQL-side domain code goes here, not in the host projects.**

## Layout

```
DeepDrftData/
├── Data/
│   ├── DeepDrftContext.cs              # EF DbContext
│   ├── DeepDrftContextFactory.cs       # Design-time factory (hard-codes ../Database/deepdrft.db)
│   └── Configurations/
│       └── TrackConfiguration.cs       # EF fluent configuration for TrackEntity
├── Migrations/                         # EF-generated migrations (namespace DeepDrftData.Migrations)
├── Repositories/
│   └── TrackRepository.cs              # Data access layer
├── TrackManager.cs                     # Service orchestrator (public interface: ITrackService)
└── DeepDrftData.csproj
```

## EF DbContext and configuration

`DeepDrftContext` targets SQLite, connection string from `appsettings.json` (`ConnectionStrings:DefaultConnection`). The design-time factory (`DeepDrftContextFactory`) hard-codes `../Database/deepdrft.db` for `dotnet ef` commands, so you can run migrations locally without a full app context.

`TrackConfiguration` uses EF fluent API:
- Table name: `track` (singular)
- Columns: snake_case (`entry_key`, `track_name`, `artist`, `album`, `genre`, `release_date`, `image_path`)
- `EntryKey`: required, max 100 (the FileDatabase vault entry id)
- `TrackName`, `Artist`: required, max 200
- `Album`, `Genre`: optional, max 200 / 100
- `ReleaseDate`: optional `DateOnly`
- `ImagePath`: optional, max 500 (currently a free-form URL string; points to images vault in future)

## Service → Repository → DbContext shape

- **Service** (`TrackManager`, implements `ITrackService`): Public contract. Takes `TrackRepository`, catches exceptions at service boundary, returns `ResultContainer<T>`.
- **Repository** (`TrackRepository`): Internal data access. Queries the DbContext. Throws on error (service catches).
- **DbContext** (`DeepDrftContext`): EF Core. Directly accessed by repository, never by service (pattern isolation).

Example:

```csharp
// TrackManager.GetPaged (public via ITrackService)
public async Task<ResultContainer<PagedResult<TrackEntity>>> GetPaged(
    int pageNumber = 1,
    int pageSize = 20,
    string? sortColumn = null,
    bool sortDescending = false,
    CancellationToken cancellationToken = default)
{
    try
    {
        var parameters = new PagingParameters<TrackEntity>
        {
            Page = pageNumber,
            PageSize = pageSize,
            OrderBy = GetOrderExpression(sortColumn),  // Maps string to LINQ expression
            IsDescending = sortDescending
        };
        var result = await _repository.GetPagedAsync(parameters, cancellationToken);
        return ResultContainer<PagedResult<TrackEntity>>.CreatePassResult(result);
    }
    catch (Exception e)
    {
        return ResultContainer<PagedResult<TrackEntity>>.CreateFailResult(e.Message);
    }
}
```

## Pagination convention

`PagingParameters<T>` holds:
- `Page` (1-based, default 1)
- `PageSize` (default 20, capped at 100)
- `OrderBy: Expression<Func<T, object>>?` (LINQ expression for sorting)
- `IsDescending`

`TrackManager.GetPaged` (the public service method) maps a string `sortColumn` (from the API query) to an expression via a switch:

```csharp
private static Expression<Func<TrackEntity, object>> GetOrderExpression(string? sortColumn)
    => sortColumn switch
    {
        "TrackName" => e => e.TrackName,
        "Artist" => e => e.Artist,
        "Album" => e => e.Album ?? "",  // Nulls sort to end
        "Genre" => e => e.Genre ?? "",
        "ReleaseDate" => e => e.ReleaseDate ?? DateOnly.MaxValue,
        _ => e => e.Id  // Default to ID
    };
```

Add new sort columns by extending this switch and the corresponding column names in the API.

## EF Migration commands

Run from the solution root:

```bash
# Add a migration
dotnet ef migrations add MigrationName --project DeepDrftData --startup-project DeepDrftPublic

# Apply to database
dotnet ef database update --project DeepDrftData --startup-project DeepDrftPublic
```

The design-time factory means you can also run `dotnet ef ... --project DeepDrftData` standalone for local development (it doesn't need the startup project).

## Migrations namespace

Migrations live in the `DeepDrftData.Migrations` namespace. Migration files are auto-generated and rarely edited by hand.

## Connection string

- **DeepDrftPublic**: `appsettings.json` → `ConnectionStrings:DefaultConnection`
- **DeepDrftContent**: `environment/connections.json` → `ConnectionStrings:DefaultConnection`
- **DeepDrftCli**: `environment/connections.json` → `CliSettings:ConnectionString`
- All point at the same database (PostgreSQL in production, SQLite for local development).

The design-time factory hard-codes the local path for `dotnet ef` commands.

## Service registration

In `DeepDrftContent/Program.cs`, `DeepDrftPublic/Program.cs`, and `DeepDrftCli/Program.cs`:

```csharp
services.AddDbContext<DeepDrftContext>(options =>
    options.UseNpgsql(configuration.GetConnectionString("DefaultConnection")));  // or UseSqlite for dev
services.AddScoped<TrackRepository>();
services.AddScoped<TrackManager>();
services.AddScoped<ITrackService>(sp => sp.GetRequiredService<TrackManager>());
```

This pattern allows callers to depend on `ITrackService` (the public interface) without knowing about `TrackManager` (the implementation). `DeepDrftContent` also registers `UnifiedTrackService` for dual-database orchestration.

## Important patterns

- **Required properties**: `EntryKey`, `TrackName`, `Artist` are `required` strings. This compile-time guarantee prevents half-built entities from reaching the database.
- **Optional properties**: `Album?`, `Genre?`, `ReleaseDate?`, `ImagePath?` are nullable. Queries must handle nulls (e.g., `Album ?? ""` in the sort expression to push nulls to end).
- **Result types**: Services return `ResultContainer<T>` or `Result` from NetBlocks. No exceptions propagate to callers — the service catches and wraps.
- **Async operations**: All database methods are async (`GetPagedAsync`, `GetByIdAsync`, etc.). Sync is not available.

## Development commands

```bash
# Build
dotnet build DeepDrftData

# Add migration (from solution root)
dotnet ef migrations add MigrationName --project DeepDrftData --startup-project DeepDrftPublic

# Apply migration
dotnet ef database update --project DeepDrftData --startup-project DeepDrftPublic

# Run from CLI (which consumes this service)
dotnet run --project DeepDrftCli -- list

# Run from Content API (dual-database host)
dotnet run --project DeepDrftContent
```

## What does NOT live here

- HTTP controllers or middleware (in host projects)
- Blazor components or rendering logic (in host projects)
- FileDatabase or binary content code (in `DeepDrftContent.Services`)
- Host-specific wiring or configuration (in host `Program.cs` / `appsettings.json`)
- Host-internal services like `UnifiedTrackService` (in host `Services/` folders)

When working with this project, focus on the data layer (repository, manager, EF configuration) and ensure all new SQL logic is testable and reusable by multiple consumers (Content API, Public API, CLI) without host-specific dependencies.
