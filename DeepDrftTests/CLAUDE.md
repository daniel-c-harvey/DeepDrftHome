# CLAUDE.md - DeepDrftTests

This file provides guidance to Claude Code (claude.ai/code) when working with the DeepDrftTests project.

## Project Overview

DeepDrftTests is a **comprehensive test suite** using **NUnit framework** that validates the FileDatabase system and related components. The tests follow SOLID principles and provide extensive coverage of the custom FileDatabase functionality.

## Architecture

### Technology Stack
- **NUnit 4.2.2**: Testing framework with modern async support
- **NUnit3TestAdapter**: Visual Studio test adapter
- **.NET 9.0**: Latest framework features
- **Coverlet**: Code coverage collection

### Project Structure
```
DeepDrftTests/
├── FileDatabaseTests.cs           # Main FileDatabase integration tests
├── MediaVaultTests.cs             # MediaVault component tests  
├── MediaVaultFactoryTests.cs      # Factory pattern tests
├── IndexSystemTests.cs            # Index management tests
├── SimpleMediaTypeRegistryTests.cs # Media type registry tests
├── UtilityTests.cs               # Utility class tests
├── ModelTests.cs                 # Model validation tests
├── TestData.cs                   # Shared test data and helpers
├── environment/                  # Test configuration files
│   └── filedatabase.json        # FileDatabase test settings
└── DeepDrftTests.csproj         # Project configuration
```

## Test Organization

### SOLID Principles Implementation
Tests follow SOLID design principles:

- **Single Responsibility**: Each test class focuses on one component
- **Open/Closed**: Base test classes allow extension without modification
- **Liskov Substitution**: All vault implementations tested consistently
- **Interface Segregation**: Tests through abstractions where possible
- **Dependency Inversion**: Tests depend on abstractions, not concretions

### DRY Pattern with Base Classes
```csharp
// Base class eliminates test setup duplication
public abstract class MediaVaultTestBase
{
    protected string TestDirectory { get; private set; }
    
    [SetUp] public virtual void SetUp() { /* Common setup */ }
    [TearDown] public virtual void TearDown() { /* Common cleanup */ }
}
```

## Key Test Classes

### FileDatabaseTests
**Core integration tests** for the main FileDatabase functionality:
- Database creation and initialization
- Vault management (create, retrieve, check existence)
- Resource operations (register, load, type safety)
- Multi-vault scenarios and error handling

### MediaVaultTests  
**Component tests** for individual MediaVault implementations:
- Entry storage and retrieval
- Media type handling (Audio, Image, Media)
- File path sanitization
- Index maintenance

### IndexSystemTests
**Index management** validation:
- DirectoryIndex creation and persistence
- VaultIndex type management
- JSON serialization/deserialization
- Index factory service operations

### MediaVaultFactoryTests
**Factory pattern** validation:
- Vault creation for different MediaVaultTypes
- Path handling and directory creation
- Type-specific vault instantiation

## Test Data Management

### TestData Class
Centralized test data and helper methods:

```csharp
public static class TestData
{
    // Real PNG bytes for authentic image testing
    public static readonly byte[] TestPngBytes = [...];
    
    // Factory methods for test objects
    public static ImageBinary CreateTestImageBinary(double aspectRatio = 1.0)
    public static AudioBinary CreateTestAudioBinary(double duration = 120.0, int bitrate = 320)
    
    // Consistent test keys
    public static class TestKeys
    {
        public const string TestImageEntry = "test";
        public const string ImageVaultKey = "img";
        public const MediaVaultType ImageVaultType = MediaVaultType.Image;
    }
}
```

## Development Commands

### Running All Tests
```bash
# Run entire test suite
dotnet test DeepDrftTests/

# Run with detailed output
dotnet test DeepDrftTests/ --verbosity normal

# Run with code coverage
dotnet test DeepDrftTests/ --collect:"XPlat Code Coverage"
```

### Running Specific Tests
```bash
# Run FileDatabase tests only
dotnet test DeepDrftTests/ --filter "FileDatabaseTests"

# Run specific test method
dotnet test DeepDrftTests/ --filter "FileDatabase_CanBeCreatedAtSpecifiedLocation"

# Run tests by category
dotnet test DeepDrftTests/ --filter "Category=Integration"
```

### Build and Clean
```bash
# Build test project
dotnet build DeepDrftTests/

# Clean test outputs
dotnet clean DeepDrftTests/
```

## Test Isolation Strategy

### Temporary Directory Management
Each test uses isolated temporary directories:
```csharp
_testDatabasePath = Path.Combine(Path.GetTempPath(), "DeepDrftTests", Guid.NewGuid().ToString());
```

### Setup and Cleanup Pattern
```csharp
[SetUp]
public void SetUp()
{
    // Create unique test directory
    Directory.CreateDirectory(_testDatabasePath);
}

[TearDown]  
public void TearDown()
{
    // Clean up test directory (with error tolerance)
    try { Directory.Delete(_testDatabasePath, true); }
    catch { /* Ignore cleanup errors */ }
}
```

## Testing Patterns

### Async Test Methods
All database operations tested with async/await:
```csharp
[Test]
public async Task FileDatabase_CanRegisterAndRetrieveAudio()
{
    var fileDatabase = await FileDatabase.FromAsync(_testDatabasePath);
    // ... async operations
}
```

### Type-Safe Media Testing
Generic type parameters ensure type safety:
```csharp
var audio = await fileDatabase.LoadResourceAsync<AudioBinary>("tracks", trackId);
Assert.That(audio, Is.Not.Null);
Assert.That(audio.Duration, Is.EqualTo(120.0));
```

### Error Scenario Coverage
Tests validate error handling patterns:
- Non-existent vaults return null
- Invalid entry IDs return null  
- File system errors are handled gracefully

## Important Testing Principles

### Authentic Test Data
Uses real PNG bytes instead of mock data for realistic testing scenarios.

### Comprehensive Coverage
Tests cover:
- Happy path scenarios
- Edge cases and error conditions
- Type safety and generics
- Async operation patterns
- File system interactions

### Performance Considerations
Tests are designed for speed:
- Minimal test data sizes
- Parallel test execution support
- Efficient cleanup strategies

When working with this test project, maintain the established patterns for test isolation, async operations, and comprehensive coverage of the FileDatabase system.