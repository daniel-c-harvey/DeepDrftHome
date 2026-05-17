# CLAUDE.md - DeepDrftTests

Guidance for working in the DeepDrftTests project (the test suite).

See the root `CLAUDE.md` for full architecture overview. This file covers what is specific to this project.

## One-line purpose

NUnit test project. Covers the FileDatabase subsystem end-to-end (the only piece of the codebase with non-trivial test coverage). References `DeepDrftContent.Services`.

## Layout

```
DeepDrftTests/
├── FileDatabaseTests.cs                # Main FileDatabase integration tests
├── MediaVaultTests.cs                  # MediaVault component tests
├── MediaVaultFactoryTests.cs           # Factory pattern tests
├── IndexSystemTests.cs                 # Index management tests
├── SimpleMediaTypeRegistryTests.cs     # Media type registry tests
├── UtilityTests.cs                     # Utility class tests (StructuralMap, FileUtils, etc.)
├── ModelTests.cs                       # Model validation tests
├── TestData.cs                         # Shared test fixtures and helpers
├── environment/
│   └── filedatabase.json               # Test-config FileDatabase settings
└── DeepDrftTests.csproj
```

## Test isolation strategy

Each test creates a unique directory under `Path.GetTempPath() + "DeepDrftTests/{Guid}"` in `[SetUp]` and best-effort deletes it in `[TearDown]`.

```csharp
[SetUp]
public void SetUp()
{
    _testDatabasePath = Path.Combine(
        Path.GetTempPath(),
        "DeepDrftTests",
        Guid.NewGuid().ToString());
    Directory.CreateDirectory(_testDatabasePath);
}

[TearDown]
public void TearDown()
{
    try { Directory.Delete(_testDatabasePath, recursive: true); }
    catch { /* Best-effort cleanup — ignore failures */ }
}
```

**New tests touching the filesystem must follow this pattern.** Do not write into the real `Database/Vaults` path. Do not use `[SetUp]` in base classes without allowing derived classes to override — some tests need custom setup.

## Core test classes

### FileDatabaseTests

Integration tests for the main FileDatabase functionality:
- Database creation and initialization at a specified path.
- Vault management (create, retrieve, check existence).
- Resource operations (register, load, type safety, edge cases).
- Multi-vault scenarios.
- Error handling (e.g., load from non-existent vault returns null).

These are the load-bearing tests. When FileDatabase semantics change, **these tests anchor the intent**. Update them in the same commit as the code change.

### MediaVaultTests

Component tests for individual vault implementations:
- Entry storage and retrieval.
- Media type handling (Audio, Image).
- File path sanitization (regex: `[^a-zA-Z0-9]` → `-`).
- Index maintenance (entries added/removed from vault index).

### IndexSystemTests

Index management validation:
- DirectoryIndex creation and persistence (JSON round-trip).
- VaultIndex type management (MediaVaultType recorded and rehydrated).
- IndexFactoryService operations.

### MediaVaultFactoryTests

Factory pattern validation:
- Vault creation for different `MediaVaultType` values.
- Path handling and directory creation.
- Type-specific vault instantiation.

### SimpleMediaTypeRegistryTests

Media type registry tests:
- Extension → MIME type mapping.
- Supported formats (wav, mp3, jpg, png, gif, etc.).

### UtilityTests

Utility class tests:
- `StructuralMap<TKey, TValue>` (JSON-based equality).
- `StructuralSet<T>` (set semantics with structural equality).
- `FileUtils` (async file I/O, chunked reading/writing).

### ModelTests

Model validation:
- Binary models can be created and serialized.
- DTOs round-trip correctly (serialize → deserialize).
- Metadata hierarchy is correct.

## TestData (shared fixtures)

Centralised test data and helper methods:

```csharp
public static class TestData
{
    // Real PNG byte buffer (16x16) — reused for mock audio testing
    public static readonly byte[] TestPngBytes = [/* 144 bytes */];

    // Factory methods
    public static ImageBinary CreateTestImageBinary(double aspectRatio = 1.0);
    public static AudioBinary CreateTestAudioBinary(double duration = 120.0, int bitrate = 320);

    // Named constants
    public static class TestKeys
    {
        public const string TestImageEntry = "test";
        public const string ImageVaultKey = "img";
        public const MediaVaultType ImageVaultType = MediaVaultType.Image;
    }
}
```

**Important**: `TestData.CreateTestAudioBinary` deliberately reuses PNG bytes as a mock audio buffer. The FileDatabase does not parse audio content — it stores any bytes with the metadata. Real WAV parsing is exercised by the CLI and content host, not the FileDatabase tests.

## Run commands

```bash
# Run all tests
dotnet test DeepDrftTests/

# Run with detailed output
dotnet test DeepDrftTests/ --verbosity normal

# Run specific test class
dotnet test DeepDrftTests/ --filter "ClassName=FileDatabaseTests"

# Run specific test method
dotnet test DeepDrftTests/ --filter "Name=FileDatabase_CanBeCreatedAtSpecifiedLocation"

# Run with code coverage (if tooling is installed)
dotnet test DeepDrftTests/ --collect:"XPlat Code Coverage"
```

## Testing patterns

### Async test methods

All database operations tested with async/await:

```csharp
[Test]
public async Task FileDatabase_CanRegisterAndRetrieveAudio()
{
    var fileDatabase = await FileDatabase.FromAsync(_testDatabasePath);
    var audio = new AudioBinary(/* params */);
    var registered = await fileDatabase.RegisterResourceAsync("tracks", "test-entry", audio);
    Assert.That(registered, Is.True);
    var loaded = await fileDatabase.LoadResourceAsync<AudioBinary>("tracks", "test-entry");
    Assert.That(loaded, Is.Not.Null);
}
```

### Type-safe media testing

Generic type parameters ensure type safety:

```csharp
var audio = await fileDatabase.LoadResourceAsync<AudioBinary>("tracks", trackId);
Assert.That(audio, Is.Not.Null);
Assert.That(audio.Duration, Is.EqualTo(120.0));
Assert.That(audio.Bitrate, Is.EqualTo(320));
```

### Error scenario coverage

Tests validate the error-swallowing contract:

```csharp
// Load from non-existent vault returns null
var result = await fileDatabase.LoadResourceAsync<AudioBinary>("non-existent", "entry");
Assert.That(result, Is.Null);

// Register to non-existent vault returns false (after creating it? depends on semantics — check code)
```

## Important testing principles

### The tests are the load-bearing documentation

When FileDatabase semantics change, especially:
- The swallow-and-return-null contract
- Entry-key sanitisation regex
- Vault-type round-trip on index
- Index-watcher reload

...the tests are where intent is anchored. Update them in the same commit as the code change.

### Authentic test data

Uses real PNG bytes instead of mock data for realistic testing scenarios. The FileDatabase doesn't care about actual audio/image content — it stores opaque buffers. But using real bytes makes the tests less misleading.

### Comprehensive coverage

Tests cover:
- Happy path scenarios (create, register, load, list).
- Edge cases (empty vault, non-existent entry, sanitisation edge cases).
- Type safety and generics.
- Async operation patterns.
- File system interactions (directory creation, file I/O, index JSON round-trip).

### Performance

Tests are designed for speed:
- Minimal test data sizes.
- Parallel test execution support (via temp directory isolation).
- Efficient cleanup.

## What is NOT tested

Be honest about coverage gaps:

- `TrackService` (Web or Content).
- `TrackController` (Web or Content).
- `TrackClient` / `TrackMediaClient` (HTTP clients).
- The audio player services (streaming, seek, interop).
- Dark-mode round-trip (cookie → settings → persistent state).
- `WavOffsetService` (byte offset → new WAV stream).
- `AudioProcessor` (WAV parsing, metadata extraction).

Any planned work in those areas should consider whether tests need to land alongside. **Testing the FileDatabase thoroughly does not mean testing everything** — it means testing the part that is most likely to break.

## Development commands

```bash
# Build
dotnet build DeepDrftTests/

# Run all tests
dotnet test DeepDrftTests/

# Run with live output
dotnet test DeepDrftTests/ -v normal

# Run a single test class
dotnet test DeepDrftTests/ --filter "ClassName=FileDatabaseTests"

# Clean test artifacts
dotnet clean DeepDrftTests/
```

## Configuration

- `environment/filedatabase.json`: Test-config FileDatabase settings (copied to output at build).
- `TestData.cs`: Centralised fixtures and helpers. Use these rather than duplicating test data.

When working with this project, treat the tests as the load-bearing specification of FileDatabase behaviour. When in doubt, read the tests first — they are clearer than the code.
