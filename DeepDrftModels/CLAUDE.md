# CLAUDE.md - DeepDrftModels

This file provides guidance to Claude Code (claude.ai/code) when working with the DeepDrftModels project.

## Project Overview

DeepDrftModels is a **shared models library** that defines common data structures, entities, DTOs, and model classes used across the entire DeepDrft solution. It serves as the data contract layer between all projects.

## Architecture

### Technology Stack
- **.NET 9.0 Class Library**: Shared library targeting .NET 9
- **Entity Framework Compatible**: Entities work with EF Core
- **JSON Serializable**: Models support JSON serialization for APIs

### Project Structure
```
DeepDrftModels/
├── Entities/           # Database entities
│   └── TrackEntity.cs  # Core track data model
├── DTOs/              # Data Transfer Objects
│   └── TrackDto.cs    # Track DTO for API transfers
├── Models/            # Shared model classes
│   ├── PagingParameters.cs  # Pagination configuration
│   └── PagedResult.cs       # Paginated result wrapper
└── DeepDrftModels.csproj   # Project file
```

## Core Models

### TrackEntity
Primary database entity for track metadata:
```csharp
public class TrackEntity
{
    public long Id { get; set; }                    // Primary key
    public required string MediaPath { get; set; }  // FileDatabase vault reference
    public required string TrackName { get; set; }  // Track title
    public required string Artist { get; set; }     // Artist name
    public string? Album { get; set; }               // Optional album
    public string? Genre { get; set; }               // Optional genre
    public DateOnly? ReleaseDate { get; set; }       // Optional release date
    public string? ImagePath { get; set; }           // Optional cover image path
}
```

### TrackDto
Data transfer object matching TrackEntity structure for API operations:
- Mirrors all TrackEntity properties
- Used for JSON serialization/deserialization
- Client-server data exchange

## Pagination System

### PagingParameters<T>
Generic pagination configuration with LINQ expression support:
```csharp
public class PagingParameters<T> : PagingParameters
{
    public Expression<Func<T, object>>? OrderBy { get; set; }  // Sorting expression
    public bool IsDescending { get; set; } = false;           // Sort direction
    public int Skip => (Page - 1) * PageSize;                 // Calculated skip count
}
```

### PagingParameters
Base pagination class with size constraints:
```csharp
public class PagingParameters
{
    public int Page { get; set; } = 1;           // Current page (1-based)
    public int PageSize { get; set; } = 20;      // Items per page (max 100)
}
```

### PagedResult<T>
Container for paginated data with metadata:
```csharp
public class PagedResult<T>
{
    public IEnumerable<T> Items { get; set; }    // Page items
    public int TotalCount { get; set; }          // Total items available
    public int Page { get; set; }                // Current page
    public int PageSize { get; set; }            // Items per page
    
    // Calculated properties
    public int TotalPages { get; }               // Total pages available
    public bool HasNextPage { get; }             // Can navigate forward
    public bool HasPreviousPage { get; }         // Can navigate backward
}
```

## Key Patterns

### Required Properties
Uses C# required modifier for essential properties:
```csharp
public required string MediaPath { get; set; }  // Compile-time requirement
```

### Nullable Reference Types
Explicit nullability throughout:
```csharp
public string? Album { get; set; }      // Optional nullable
public required string Artist { get; set; }  // Required non-null
```

### Generic Type Conversion
PagedResult supports type transformation:
```csharp
public static PagedResult<T> From<TOther>(PagedResult<TOther> other, IEnumerable<T> items)
```

### Expression-Based Sorting
Type-safe LINQ expressions for dynamic sorting:
```csharp
parameters.OrderBy = entity => entity.TrackName;  // Compile-time checked
```

## Integration Points

### Entity Framework
- `TrackEntity` configured for EF Core in `DeepDrftWeb.Data.Configurations`
- Long Id as primary key for SQLite compatibility
- DateOnly support for release dates

### API Serialization
- All models JSON-serializable for web API usage
- DTO pattern separates data transfer from domain models

### Cross-Project Usage
Referenced by:
- **DeepDrftWeb**: Entity Framework, services, repositories
- **DeepDrftWeb.Client**: API client communication
- **DeepDrftContent**: API DTOs (potential future usage)
- **DeepDrftTests**: Test data and assertions

## External Dependencies

### NetBlocks Library
Some projects reference `NetBlocks.Models` for:
- `ResultContainer<T>`: Consistent result handling pattern
- `ApiResult<T>`: API response wrapper
- `Result`: Simple operation result

## Development Notes

### Page Size Limits
Maximum page size enforced at 100 items to prevent performance issues:
```csharp
set => _pageSize = value > _maxPageSize ? _maxPageSize : value;
```

### 1-Based Pagination
Page numbers start at 1 (not 0) following common UI patterns:
```csharp
public int Skip => (Page - 1) * PageSize;  // Convert to 0-based for queries
```

### Immutable Design
Models favor immutability where possible, using init-only properties and required fields.

When working with this project, maintain consistency in data models across the solution and preserve the established patterns for pagination, nullability, and type safety.