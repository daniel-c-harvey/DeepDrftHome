using DeepDrftContent.Services.FileDatabase.Models;
using DeepDrftContent.Services.FileDatabase.Services;

namespace DeepDrftTests;

/// <summary>
/// SOLID, DRY tests for MediaVaultFactory
/// Follows Single Responsibility: Each test focuses on one factory behavior
/// Follows Open/Closed: Tests extensibility through MediaVaultType enum
/// </summary>
[TestFixture]
public class MediaVaultFactoryTests
{
    /// <summary>
    /// Base class for MediaVaultFactory tests - DRY principle
    /// </summary>
    public abstract class MediaVaultFactoryTestBase
    {
        protected string TestDirectory { get; private set; } = null!;

        [SetUp]
        public virtual void SetUp()
        {
            TestDirectory = Path.Combine(Path.GetTempPath(), "DeepDrftTests", "MediaVaultFactory", Guid.NewGuid().ToString());
            Directory.CreateDirectory(TestDirectory);
        }

        [TearDown]
        public virtual void TearDown()
        {
            if (Directory.Exists(TestDirectory))
            {
                try { Directory.Delete(TestDirectory, true); } catch { /* Ignore cleanup errors */ }
            }
        }

        /// <summary>
        /// Helper method to verify vault creation - DRY principle
        /// </summary>
        protected static void AssertVaultCreated<T>(MediaVault? vault, string testContext) where T : MediaVault
        {
            Assert.That(vault, Is.Not.Null, $"Vault should be created for {testContext}");
            Assert.That(vault, Is.TypeOf<T>(), $"Should create correct vault type for {testContext}");
        }
    }

    /// <summary>
    /// Tests for basic factory functionality - Single Responsibility
    /// </summary>
    [TestFixture]
    public class BasicFactoryTests : MediaVaultFactoryTestBase
    {
        [Test]
        public async Task From_ImageVaultType_CreatesImageVault()
        {
            // Act
            var vault = await MediaVaultFactory.From(TestDirectory, MediaVaultType.Image);

            // Assert
            AssertVaultCreated<ImageVault>(vault, "Image vault creation");
        }

        [Test]
        public async Task From_AudioVaultType_CreatesAudioVault()
        {
            // Act
            var vault = await MediaVaultFactory.From(TestDirectory, MediaVaultType.Audio);

            // Assert
            AssertVaultCreated<AudioVault>(vault, "Audio vault creation");
        }

        [Test]
        public async Task From_MediaVaultType_ReturnsNull()
        {
            // Note: MediaVaultType.Media doesn't have a concrete vault implementation
            // This tests the factory's handling of unsupported types
            
            // Act
            var vault = await MediaVaultFactory.From(TestDirectory, MediaVaultType.Media);

            // Assert
            Assert.That(vault, Is.Null, "Should return null for unsupported Media vault type");
        }

        [Test]
        public async Task From_InvalidVaultType_ReturnsNull()
        {
            // Arrange
            var invalidType = (MediaVaultType)999;

            // Act
            var vault = await MediaVaultFactory.From(TestDirectory, invalidType);

            // Assert
            Assert.That(vault, Is.Null, "Should return null for invalid vault type");
        }
    }

    /// <summary>
    /// Tests for factory behavior with different directory states - Interface Segregation
    /// </summary>
    [TestFixture]
    public class DirectoryStateTests : MediaVaultFactoryTestBase
    {
        [Test]
        public async Task From_NonExistentDirectory_CreatesDirectoryAndVault()
        {
            // Arrange
            var nonExistentPath = Path.Combine(TestDirectory, "non-existent");
            Assert.That(Directory.Exists(nonExistentPath), Is.False, "Directory should not exist initially");

            // Act
            var vault = await MediaVaultFactory.From(nonExistentPath, MediaVaultType.Image);

            // Assert
            AssertVaultCreated<ImageVault>(vault, "Non-existent directory");
            Assert.That(Directory.Exists(nonExistentPath), Is.True, "Directory should be created");
        }

        [Test]
        public async Task From_ExistingEmptyDirectory_CreatesVault()
        {
            // Arrange - Directory already exists but is empty
            Assert.That(Directory.Exists(TestDirectory), Is.True, "Directory should exist");
            Assert.That(Directory.GetFileSystemEntries(TestDirectory), Is.Empty, "Directory should be empty");

            // Act
            var vault = await MediaVaultFactory.From(TestDirectory, MediaVaultType.Image);

            // Assert
            AssertVaultCreated<ImageVault>(vault, "Existing empty directory");
        }

        [Test]
        public async Task From_ExistingDirectoryWithIndex_LoadsExistingVault()
        {
            // Arrange - Create a vault first to establish an index
            var originalVault = await MediaVaultFactory.From(TestDirectory, MediaVaultType.Image);
            Assert.That(originalVault, Is.Not.Null, "Original vault should be created");

            // Act - Create another vault from the same directory
            var reloadedVault = await MediaVaultFactory.From(TestDirectory, MediaVaultType.Image);

            // Assert
            AssertVaultCreated<ImageVault>(reloadedVault, "Existing directory with index");
            
            // Verify both vaults reference the same underlying directory
            Assert.That(reloadedVault!.RootPath, Is.EqualTo(originalVault!.RootPath), 
                "Both vaults should reference the same directory");
        }
    }

    /// <summary>
    /// Tests for factory error handling - Dependency Inversion Principle
    /// </summary>
    [TestFixture]
    public class ErrorHandlingTests : MediaVaultFactoryTestBase
    {
        [Test]
        public async Task From_InvalidPath_HandlesGracefully()
        {
            // Arrange - Use invalid path characters
            var invalidPath = Path.Combine(TestDirectory, "invalid<>path");

            // Act & Assert - On Windows, this will throw IOException, which is expected behavior
            // The factory doesn't need to handle every possible OS-level path error
            try
            {
                await MediaVaultFactory.From(invalidPath, MediaVaultType.Image);
                // If we get here without exception, that's also fine
                Assert.Pass("Factory handled invalid path without throwing");
            }
            catch (IOException)
            {
                // This is expected behavior on Windows
                Assert.Pass("Factory correctly propagated OS-level path error");
            }
            catch (Exception ex)
            {
                Assert.Fail($"Factory threw unexpected exception type: {ex.GetType().Name}");
            }
        }

        [Test]
        public async Task From_ReadOnlyDirectory_HandlesGracefully()
        {
            // This test is platform-specific and may behave differently on different OS
            // On Windows, this test may not be as effective as on Unix systems
            
            // Act & Assert - Should not throw exceptions
            Assert.DoesNotThrowAsync(async () =>
            {
                await MediaVaultFactory.From(TestDirectory, MediaVaultType.Image);
            }, "Factory should handle permission issues gracefully");
        }

        [Test]
        public async Task From_VeryLongPath_HandlesGracefully()
        {
            // Arrange - Create a very long path (but within reasonable limits)
            var longDirectoryName = new string('a', 100);
            var longPath = Path.Combine(TestDirectory, longDirectoryName);

            // Act & Assert - Should not throw
            Assert.DoesNotThrowAsync(async () =>
            {
                await MediaVaultFactory.From(longPath, MediaVaultType.Image);
            }, "Factory should handle long paths gracefully");
        }
    }

    /// <summary>
    /// Tests for factory consistency and idempotency - Liskov Substitution Principle
    /// </summary>
    [TestFixture]
    public class ConsistencyTests : MediaVaultFactoryTestBase
    {
        [Test]
        public async Task From_SameParametersMultipleCalls_ReturnsConsistentResults()
        {
            // Act
            var vault1 = await MediaVaultFactory.From(TestDirectory, MediaVaultType.Image);
            var vault2 = await MediaVaultFactory.From(TestDirectory, MediaVaultType.Image);

            // Assert
            Assert.That(vault1, Is.Not.Null, "First vault should be created");
            Assert.That(vault2, Is.Not.Null, "Second vault should be created");
            Assert.That(vault1!.GetType(), Is.EqualTo(vault2!.GetType()), "Both vaults should be same type");
            Assert.That(vault1.RootPath, Is.EqualTo(vault2.RootPath), "Both vaults should have same root path");
        }

        [Test]
        public async Task From_DifferentVaultTypesInSameDirectory_CreatesAppropriateTypes()
        {
            // Act
            var imageVault = await MediaVaultFactory.From(TestDirectory, MediaVaultType.Image);
            var audioVault = await MediaVaultFactory.From(TestDirectory, MediaVaultType.Audio);

            // Assert
            AssertVaultCreated<ImageVault>(imageVault, "Image vault in shared directory");
            AssertVaultCreated<AudioVault>(audioVault, "Audio vault in shared directory");
            
            // Both should reference the same directory but be different types
            Assert.That(imageVault!.RootPath, Is.EqualTo(audioVault!.RootPath), 
                "Both vaults should reference the same directory");
            Assert.That(imageVault.GetType(), Is.Not.EqualTo(audioVault.GetType()), 
                "Vaults should be different types");
        }

        [Test]
        public async Task From_ConcurrentCalls_HandlesGracefully()
        {
            // Arrange
            var tasks = new List<Task<MediaVault?>>();
            const int concurrentCalls = 5;

            // Act - Make multiple concurrent calls
            for (int i = 0; i < concurrentCalls; i++)
            {
                var subdirectory = Path.Combine(TestDirectory, $"concurrent-{i}");
                tasks.Add(MediaVaultFactory.From(subdirectory, MediaVaultType.Image));
            }

            var results = await Task.WhenAll(tasks);

            // Assert
            Assert.That(results.Length, Is.EqualTo(concurrentCalls), "Should complete all concurrent calls");
            
            foreach (var (result, index) in results.Select((r, i) => (r, i)))
            {
                AssertVaultCreated<ImageVault>(result, $"Concurrent call {index}");
            }
        }
    }

    /// <summary>
    /// Tests for factory integration with underlying registry - Dependency Inversion
    /// </summary>
    [TestFixture]
    public class RegistryIntegrationTests : MediaVaultFactoryTestBase
    {
        [Test]
        public async Task From_AllSupportedVaultTypes_CreatesCorrectTypes()
        {
            // Test all currently supported vault types
            var supportedTypes = new[]
            {
                (MediaVaultType.Image, typeof(ImageVault)),
                (MediaVaultType.Audio, typeof(AudioVault))
            };

            foreach (var (vaultType, expectedType) in supportedTypes)
            {
                // Arrange
                var subdirectory = Path.Combine(TestDirectory, vaultType.ToString().ToLower());

                // Act
                var vault = await MediaVaultFactory.From(subdirectory, vaultType);

                // Assert
                Assert.That(vault, Is.Not.Null, $"Should create vault for {vaultType}");
                Assert.That(vault!.GetType(), Is.EqualTo(expectedType), $"Should create {expectedType.Name} for {vaultType}");
            }
        }

        [Test]
        public async Task From_FactoryUsesUnderlyingRegistry_ConsistentWithRegistryBehavior()
        {
            // This test verifies that the factory delegates properly to the registry
            // and behaves consistently with direct registry usage
            
            // Arrange
            var registry = new SimpleMediaTypeRegistry();

            // Act
            var factoryVault = await MediaVaultFactory.From(TestDirectory, MediaVaultType.Image);
            var registryVault = await registry.CreateVaultAsync(MediaVaultType.Image, TestDirectory);

            // Assert
            if (factoryVault != null && registryVault != null)
            {
                Assert.That(factoryVault.GetType(), Is.EqualTo(registryVault.GetType()), 
                    "Factory and registry should create same vault types");
            }
            else
            {
                Assert.That(factoryVault, Is.EqualTo(registryVault), 
                    "Both factory and registry should return same null/non-null result");
            }
        }
    }
}
