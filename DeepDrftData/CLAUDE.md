# CLAUDE.md - DeepDrftWeb.Services

Guidance for working in the DeepDrftWeb.Services project (the SQL-side domain logic).

See the root `CLAUDE.md` for full architecture overview. This file covers what is specific to this project.

## One-line purpose

SQL-side domain logic for tracks. EF Core context, configurations, migrations, repository, service, design-time factory. Consumed by both `DeepDrftWeb` (the host) and `DeepDrftCli` (the admin CLI).

## Why this project exists

Separating domain logic from the host so the CLI can reuse `TrackService` / `TrackRepository` / `DeepDrftContext` without referencing the ASP.NET host. The CLI is a local admin tool, not a network client — it needs direct DB access.

**New SQL-side domain code goes here, not in `DeepDrftWeb`.**

## Layout

```
DeepDrftWeb.Services/
├── Data/
│   ├── DeepDrftContext.cs              # EF DbContext
│   ├── DeepDrftContextFactory.cs       # Design-time factory (hard-codes ../Database/deepdrft.db)
│   └── Configurations/
│       └── TrackConfiguration.cs       # EF fluent configuration for TrackEntity
├── Migrations/                         # EF-generated migrations (namespace DeepDrftWeb.Migrations)
├── Repositories/
│   └── TrackRepository.cs              # Data access layer
├── TrackService.cs                     # Service orchestrator
└── DeepDrftWeb.Services.csproj
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

## Controller → Service → Repository → DbContext shape

- **Service** (public contract): Takes `TrackRepository`, catches exceptions at service boundary, returns `Result` / `ResultContainer<T>`.
- **Repository** (internal): Queries the DbContext. Throws on error (service catches).
- **DbContext** (EF): Directly accessed by repository, never by service (pattern isolation).

Example:

```csharp
// TrackService.GetPaged (public)
public async Task<ResultContainer<PagedResult<TrackEntity>>> GetPaged(
    int pageNumber = 1,
    int pageSize = 20,
    string? sortColumn = null,
    bool sortDescending = false)
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
        var result = await _repository.GetPagedAsync(parameters);
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

`TrackService.GetPaged` maps a string `sortColumn` (from the API query) to an expression via a switch:

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
dotnet ef migrations add MigrationName --project DeepDrftWeb.Services --startup-project DeepDrftWeb

# Apply to database
dotnet ef database update --project DeepDrftWeb.Services --startup-project DeepDrftWeb
```

The design-time factory means you can also run `dotnet ef ... --project DeepDrftWeb.Services` standalone for local development (it doesn't need the startup project).

## Migrations namespace

Migrations live in the `DeepDrftWeb.Migrations` namespace (a legacy name retained for history continuity). Migration files are auto-generated and rarely edited by hand.

## Connection string

- **Web side**: `appsettings.json` → `ConnectionStrings:DefaultConnection`
- **CLI side**: `environment/connections.json` → `CliSettings:ConnectionString`
- Both point at `../Database/deepdrft.db`

The design-time factory hard-codes the path for `dotnet ef` commands.

## Service registration

In `DeepDrftWeb/Program.cs` and `DeepDrftCli/Program.cs`:

```csharp
services.AddDbContext<DeepDrftContext>(options =>
    options.UseSqlite(configuration.GetConnectionString("DefaultConnection")));
services.AddScoped<TrackRepository>();
services.AddScoped<TrackService>();
```

## Important patterns

- **Required properties**: `EntryKey`, `TrackName`, `Artist` are `required` strings. This compile-time guarantee prevents half-built entities from reaching the database.
- **Optional properties**: `Album?`, `Genre?`, `ReleaseDate?`, `ImagePath?` are nullable. Queries must handle nulls (e.g., `Album ?? ""` in the sort expression to push nulls to end).
- **Result types**: Services return `ResultContainer<T>` or `Result` from NetBlocks. No exceptions propagate to callers — the service catches and wraps.
- **Async operations**: All database methods are async (`GetPagedAsync`, `GetByIdAsync`, etc.). Sync is not available.

## Development commands

```bash
# Build
dotnet build DeepDrftWeb.Services

# Add migration (from solution root)
dotnet ef migrations add MigrationName --project DeepDrftWeb.Services --startup-project DeepDrftWeb

# Apply migration
dotnet ef database update --project DeepDrftWeb.Services --startup-project DeepDrftWeb

# Run from CLI (which consumes this service)
dotnet run --project DeepDrftCli -- list
```

## What does NOT live here

- HTTP controllers or middleware
- Blazor components or rendering logic
- FileDatabase or binary content code (that's in `DeepDrftContent.Services`)
- Configuration for the web host (`appsettings.json` stays in `DeepDrftWeb`)

When working with this project, focus on the data layer (repository, service, EF configuration) and ensure all new SQL logic is testable and reusable by both the web host and the CLI.
