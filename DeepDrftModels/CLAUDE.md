# CLAUDE.md - DeepDrftModels

Guidance for working in the DeepDrftModels project (shared contracts across all projects).

See the root `CLAUDE.md` for full architecture overview. This file covers what is specific to this project.

## One-line purpose

Shared contracts. Entities, DTOs, pagination types. Every project references this; nothing else references the projects that reference this.

## Layout

```
DeepDrftModels/
├── Entities/
│   └── TrackEntity.cs          # Database entity for tracks
├── DTOs/
│   └── TrackDto.cs             # DTO mirror of TrackEntity
├── Models/
│   ├── PagingParameters.cs      # Pagination configuration (base + generic)
│   └── PagedResult.cs           # Paginated result wrapper
└── DeepDrftModels.csproj
```

## TrackEntity (the core entity)

The single source of truth for track metadata. Fields:

```csharp
public long Id { get; set; }                      // Primary key (auto-assigned by SQLite)
public required string EntryKey { get; set; }     // FileDatabase entry id (max 100)
public required string TrackName { get; set; }    // Track title (max 200)
public required string Artist { get; set; }       // Artist name (max 200)
public string? Album { get; set; }                // Optional album (max 200)
public string? Genre { get; set; }                // Optional genre (max 100)
public DateOnly? ReleaseDate { get; set; }        // Optional release date
public string? ImagePath { get; set; }            // Optional image URL (max 500)
```

**No `MediaPath` field exists.** That was a legacy name. The field is `EntryKey`.

The `EntryKey` is the link to binary content — it's the entry id in the `tracks` vault inside FileDatabase. The database column is `entry_key` (snake_case).

Convention: required reference fields use `required` modifier; optional reference fields are `?`. Don't relax `required` — it's the compile-time guarantee that prevents half-built entities from reaching the database.

## TrackDto

Mirrors `TrackEntity` structure (identical fields, same nullability). Used where DTO/entity separation is needed for serialisation. In practice, both flow over the wire today, but the separation is available if APIs need to diverge (e.g., hide `Id` in responses).

## Pagination system

### PagingParameters (base)

```csharp
public class PagingParameters
{
    public int Page { get; set; } = 1;         // Current page (1-based)
    public int PageSize { get; set; } = 20;    // Items per page (default 20, max 100)
}
```

`PageSize` setter enforces a hard ceiling at 100 — prevents clients from requesting huge pages that would tank performance.

### PagingParameters<T> (generic)

```csharp
public class PagingParameters<T> : PagingParameters
{
    public Expression<Func<T, object>>? OrderBy { get; set; }  // Type-safe sort expression
    public bool IsDescending { get; set; }                     // Sort direction
    public int Skip => (Page - 1) * PageSize;                  // Calculated (0-based)
}
```

Used by services to build type-safe LINQ queries. The service takes a string sort column from the API, maps it to an `Expression<Func<T, object>>`, and passes it in `OrderBy`. The repository applies the expression to the DbSet.

### PagedResult<T>

```csharp
public class PagedResult<T>
{
    public IEnumerable<T> Items { get; set; }     // Current page items
    public int TotalCount { get; set; }           // Total items available
    public int Page { get; set; }                 // Current page
    public int PageSize { get; set; }             // Items per page
    
    // Calculated properties
    public int TotalPages => (TotalCount + PageSize - 1) / PageSize;
    public bool HasNextPage => Page < TotalPages;
    public bool HasPreviousPage => Page > 1;
}
```

Returned by service `GetPaged` methods. Includes a static factory `From<TOther>` for cross-type mapping (e.g., `PagedResult<TrackEntity>` → `PagedResult<TrackDto>`).

## Key patterns

### Required fields

Essential fields use the `required` modifier. This is a compile-time guarantee that prevents half-built entities from being instantiated.

```csharp
public required string EntryKey { get; set; }
public required string Artist { get; set; }
```

### Nullable reference types

Explicit nullability throughout:

```csharp
public string? Album { get; set; }              // Optional, can be null
public required string Artist { get; set; }     // Required, must be assigned
```

### Expression-based sorting

Type-safe LINQ expressions for dynamic sorting. No string-based `OrderBy` at runtime:

```csharp
parameters.OrderBy = entity => entity.TrackName;  // Compile-time checked
```

Services map string column names to expressions in a switch, so only valid sorts are possible.

### Pagination defaults

- `Page`: 1 (1-based)
- `PageSize`: 20 (per-item default)
- `PageSize` max: 100 (hard cap, enforced by setter)

Client requests outside these bounds are clamped by the API controller before being passed to the service.

## Integration points

- **EF Core**: `TrackEntity` is configured in `DeepDrftWeb.Services.Data.Configurations.TrackConfiguration`.
- **API serialisation**: All models JSON-serializable. Controllers return `ActionResult<ApiResultDto<PagedResult<TrackEntity>>>`.
- **Cross-project usage**:
  - `DeepDrftWeb`: Server render preload, controller responses.
  - `DeepDrftWeb.Client`: HTTP client deserialization, UI display.
  - `DeepDrftContent.Services`: `TrackService.AddTrackFromWavAsync` returns populated `TrackEntity`.
  - `DeepDrftCli`: Entity display, table formatting.
  - `DeepDrftTests`: Test assertions.

## NetBlocks transitivity

This project does not directly reference NetBlocks. The result types (`Result`, `ResultContainer<T>`, `ApiResult<T>`, `ApiResultDto<T>`) come into services and clients directly via their own NetBlocks references. Models stay independent of result wrappers — they are serializable POCOs.

## Important conventions

- **Column naming**: Entity property `EntryKey` maps to table column `entry_key` (snake_case, configured in EF).
- **Table name**: `track` (singular, configured in EF).
- **Required fields**: Properties with `required` modifier cannot be left unassigned.
- **Page numbers**: 1-based (page 1 is the first page). The `Skip` calculation converts to 0-based for LINQ (`(Page - 1) * PageSize`).

## Development commands

```bash
# Build
dotnet build DeepDrftModels

# No tests live here (models are simple POCOs)
# Tests that use these models live in DeepDrftTests/
```

When working with this project, maintain the required/optional distinction rigorously, keep the pagination contract tight, and remember that this project is the interface between all other projects — changes here ripple everywhere.
