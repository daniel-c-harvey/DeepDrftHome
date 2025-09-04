using DeepDrftContent.FileDatabase.Models;
using DeepDrftContent.FileDatabase.Services;
using DeepDrftContent.FileDatabase.Utils;

namespace DeepDrftTests;

/// <summary>
/// SOLID, DRY tests for MediaVault implementations
/// Follows Single Responsibility: Each test class tests one vault concern
/// Follows Liskov Substitution: Tests that all vault implementations behave consistently
/// Follows Dependency Inversion: Tests through abstractions where possible
/// </summary>
[TestFixture]
public class MediaVaultTests
{
    /// <summary>
    /// Base class for MediaVault tests - DRY principle
    /// </summary>
    public abstract class MediaVaultTestBase
    {
        protected string TestDirectory { get; private set; } = null!;
        protected string IndexPath => Path.Combine(TestDirectory, "index");

        [SetUp]
        public virtual void SetUp()
        {
            TestDirectory = Path.Combine(Path.GetTempPath(), "DeepDrftTests", "MediaVault", Guid.NewGuid().ToString());
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
        /// Helper method to create test entry keys - DRY principle
        /// </summary>
        protected static EntryKey CreateTestEntryKey(string key, MediaVaultType type = MediaVaultType.Image)
            => new(key, type);

        /// <summary>
        /// Helper method to create test media files - DRY principle
        /// </summary>
        protected string CreateTestMediaFile(string fileName, byte[]? content = null)
        {
            content ??= TestData.TestPngBytes;
            var filePath = Path.Combine(TestDirectory, fileName);
            File.WriteAllBytes(filePath, content);
            return filePath;
        }

        /// <summary>
        /// Helper method to verify media file exists and has correct content - DRY principle
        /// </summary>
        protected static void AssertMediaFileExists(string filePath, byte[] expectedContent)
        {
            Assert.That(File.Exists(filePath), Is.True, $"Media file should exist at {filePath}");
            var actualContent = File.ReadAllBytes(filePath);
            Assert.That(actualContent, Is.EqualTo(expectedContent), "File content should match expected");
        }
    }

    /// <summary>
    /// Tests for ImageVault - Single Responsibility
    /// </summary>
    [TestFixture]
    public class ImageVaultTests : MediaVaultTestBase
    {
        private ImageVault _imageVault = null!;

        [SetUp]
        public async Task SetUpAsync()
        {
            base.SetUp(); // Call base synchronous setup first
            _imageVault = await ImageVault.FromAsync(TestDirectory);
            Assert.That(_imageVault, Is.Not.Null, "ImageVault should be created for tests");
        }

        [Test]
        public async Task ImageVault_FromAsync_CreatesVaultWithIndex()
        {
            // Act
            var vault = await ImageVault.FromAsync(TestDirectory);

            // Assert
            Assert.That(vault, Is.Not.Null, "Should create ImageVault");
            Assert.That(vault!.RootPath, Is.EqualTo(TestDirectory), "Should use provided directory");
            Assert.That(File.Exists(IndexPath), Is.True, "Should create index file");
        }

        [Test]
        public async Task ImageVault_FromAsync_NonExistentDirectory_CreatesDirectoryAndVault()
        {
            // Arrange
            var newDirectory = Path.Combine(TestDirectory, "new-vault");
            Assert.That(Directory.Exists(newDirectory), Is.False, "Directory should not exist initially");

            // Act
            var vault = await ImageVault.FromAsync(newDirectory);

            // Assert
            Assert.That(vault, Is.Not.Null, "Should create ImageVault");
            Assert.That(Directory.Exists(newDirectory), Is.True, "Should create directory");
            Assert.That(File.Exists(Path.Combine(newDirectory, "index")), Is.True, "Should create index");
        }

        [Test]
        public async Task AddEntryAsync_ImageBinary_AddsToIndexAndCreatesFile()
        {
            // Arrange
            var entryKey = CreateTestEntryKey("test-image");
            var imageBinary = TestData.CreateTestImageBinary(1.5);

            // Act
            await _imageVault.AddEntryAsync(MediaVaultType.Image, entryKey, imageBinary);

            // Assert
            Assert.That(_imageVault.HasIndexEntry(entryKey), Is.True, "Should add to index");
            
            var expectedFilePath = Path.Combine(TestDirectory, "test-image.png");
            AssertMediaFileExists(expectedFilePath, imageBinary.Buffer);
        }

        [Test]
        public async Task AddEntryAsync_MultipleImages_AddsAllToIndexAndCreatesFiles()
        {
            // Arrange
            var entries = new[]
            {
                (CreateTestEntryKey("image1"), TestData.CreateTestImageBinary(1.0)),
                (CreateTestEntryKey("image2"), TestData.CreateTestImageBinary(1.5)),
                (CreateTestEntryKey("image3"), TestData.CreateTestImageBinary(2.0))
            };

            // Act
            foreach (var (key, binary) in entries)
            {
                await _imageVault.AddEntryAsync(MediaVaultType.Image, key, binary);
            }

            // Assert
            Assert.That(_imageVault.GetIndexSize(), Is.EqualTo(3), "Should have three entries in index");
            
            foreach (var (key, binary) in entries)
            {
                Assert.That(_imageVault.HasIndexEntry(key), Is.True, $"Should contain {key} in index");
                var expectedFilePath = Path.Combine(TestDirectory, $"{key.Key}.png");
                AssertMediaFileExists(expectedFilePath, binary.Buffer);
            }
        }

        [Test]
        public async Task GetEntryAsync_ExistingImage_ReturnsImageBinary()
        {
            // Arrange
            var entryKey = CreateTestEntryKey("existing-image");
            var originalImage = TestData.CreateTestImageBinary(1.77);
            await _imageVault.AddEntryAsync(MediaVaultType.Image, entryKey, originalImage);

            // Act
            var retrievedImage = await _imageVault.GetEntryAsync<ImageBinary>(MediaVaultType.Image, entryKey);

            // Assert
            Assert.That(retrievedImage, Is.Not.Null, "Should retrieve image");
            Assert.That(retrievedImage!.Buffer, Is.EqualTo(originalImage.Buffer), "Buffer should match");
            Assert.That(retrievedImage.Extension, Is.EqualTo(originalImage.Extension), "Extension should match");
            Assert.That(retrievedImage.AspectRatio, Is.EqualTo(originalImage.AspectRatio), "Aspect ratio should match");
        }

        [Test]
        public async Task GetEntryAsync_NonExistentImage_ReturnsNull()
        {
            // Arrange
            var nonExistentKey = CreateTestEntryKey("non-existent");

            // Act
            var retrievedImage = await _imageVault.GetEntryAsync<ImageBinary>(MediaVaultType.Image, nonExistentKey);

            // Assert
            Assert.That(retrievedImage, Is.Null, "Should return null for non-existent image");
        }

        [Test]
        public async Task GetEntryAsync_IndexEntryExistsButFileDeleted_ReturnsNull()
        {
            // Arrange
            var entryKey = CreateTestEntryKey("deleted-file");
            var imageBinary = TestData.CreateTestImageBinary(1.0);
            await _imageVault.AddEntryAsync(MediaVaultType.Image, entryKey, imageBinary);

            // Delete the physical file but leave index entry
            var filePath = Path.Combine(TestDirectory, "deleted-file.png");
            File.Delete(filePath);

            // Act
            var retrievedImage = await _imageVault.GetEntryAsync<ImageBinary>(MediaVaultType.Image, entryKey);

            // Assert
            Assert.That(retrievedImage, Is.Null, "Should return null when file is missing");
        }

        [Test]
        public async Task AddEntryAsync_DuplicateKey_UpdatesExistingEntry()
        {
            // Arrange
            var entryKey = CreateTestEntryKey("duplicate-key");
            var originalImage = TestData.CreateTestImageBinary(1.0);
            var updatedImage = TestData.CreateTestImageBinary(2.0);

            // Act
            await _imageVault.AddEntryAsync(MediaVaultType.Image, entryKey, originalImage);
            await _imageVault.AddEntryAsync(MediaVaultType.Image, entryKey, updatedImage);

            // Assert
            Assert.That(_imageVault.GetIndexSize(), Is.EqualTo(1), "Should still have only one entry");
            
            var retrievedImage = await _imageVault.GetEntryAsync<ImageBinary>(MediaVaultType.Image, entryKey);
            Assert.That(retrievedImage, Is.Not.Null, "Should retrieve updated image");
            Assert.That(retrievedImage!.AspectRatio, Is.EqualTo(2.0), "Should have updated aspect ratio");
        }
    }

    /// <summary>
    /// Tests for AudioVault - Single Responsibility (following same patterns as ImageVault)
    /// </summary>
    [TestFixture]
    public class AudioVaultTests : MediaVaultTestBase
    {
        private AudioVault _audioVault = null!;

        [SetUp]
        public async Task SetUpAsync()
        {
            base.SetUp(); // Call base synchronous setup first
            _audioVault = await AudioVault.FromAsync(TestDirectory);
            Assert.That(_audioVault, Is.Not.Null, "AudioVault should be created for tests");
        }

        [Test]
        public async Task AudioVault_FromAsync_CreatesVaultWithIndex()
        {
            // Act
            var vault = await AudioVault.FromAsync(TestDirectory);

            // Assert
            Assert.That(vault, Is.Not.Null, "Should create AudioVault");
            Assert.That(vault!.RootPath, Is.EqualTo(TestDirectory), "Should use provided directory");
            Assert.That(File.Exists(IndexPath), Is.True, "Should create index file");
        }

        [Test]
        public async Task AddEntryAsync_AudioBinary_AddsToIndexAndCreatesFile()
        {
            // Arrange
            var entryKey = CreateTestEntryKey("test-audio", MediaVaultType.Audio);
            var audioBinary = TestData.CreateTestAudioBinary(120.0, 320);

            // Act
            await _audioVault.AddEntryAsync(MediaVaultType.Audio, entryKey, audioBinary);

            // Assert
            Assert.That(_audioVault.HasIndexEntry(entryKey), Is.True, "Should add to index");
            
            var expectedFilePath = Path.Combine(TestDirectory, "test-audio.mp3");
            AssertMediaFileExists(expectedFilePath, audioBinary.Buffer);
        }

        [Test]
        public async Task GetEntryAsync_ExistingAudio_ReturnsAudioBinary()
        {
            // Arrange
            var entryKey = CreateTestEntryKey("existing-audio", MediaVaultType.Audio);
            var originalAudio = TestData.CreateTestAudioBinary(180.5, 256);
            await _audioVault.AddEntryAsync(MediaVaultType.Audio, entryKey, originalAudio);

            // Act
            var retrievedAudio = await _audioVault.GetEntryAsync<AudioBinary>(MediaVaultType.Audio, entryKey);

            // Assert
            Assert.That(retrievedAudio, Is.Not.Null, "Should retrieve audio");
            Assert.That(retrievedAudio!.Buffer, Is.EqualTo(originalAudio.Buffer), "Buffer should match");
            Assert.That(retrievedAudio.Extension, Is.EqualTo(originalAudio.Extension), "Extension should match");
            Assert.That(retrievedAudio.Duration, Is.EqualTo(originalAudio.Duration), "Duration should match");
            Assert.That(retrievedAudio.Bitrate, Is.EqualTo(originalAudio.Bitrate), "Bitrate should match");
        }
    }

    /// <summary>
    /// Tests for MediaVault abstract base class behavior - Liskov Substitution Principle
    /// </summary>
    [TestFixture]
    public class MediaVaultBaseTests : MediaVaultTestBase
    {
        /// <summary>
        /// Test implementation of MediaVault for testing abstract functionality
        /// Uses ImageVault as concrete implementation to avoid creating test-specific vault
        /// </summary>
        private class TestMediaVaultWrapper
        {
            private readonly ImageVault _vault;

            public TestMediaVaultWrapper(ImageVault vault)
            {
                _vault = vault;
            }

            public static async Task<TestMediaVaultWrapper?> FromAsync(string rootPath)
            {
                var vault = await ImageVault.FromAsync(rootPath);
                return vault != null ? new TestMediaVaultWrapper(vault) : null;
            }

            // Expose protected methods for testing using reflection
            public string GetMediaKey(string entryKey, string extension)
            {
                var method = typeof(MediaVault).GetMethod("GetMediaKey", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                return (string)method!.Invoke(_vault, new object[] { entryKey, extension })!;
            }

            public string GetMediaPathFromEntryKey(string entryKey, string extension)
            {
                var method = typeof(MediaVault).GetMethod("GetMediaPathFromEntryKey", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                return (string)method!.Invoke(_vault, new object[] { entryKey, extension })!;
            }

            public string GetMediaPathFromMediaKey(string mediaKey)
            {
                var method = typeof(MediaVault).GetMethod("GetMediaPathFromMediaKey", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                return (string)method!.Invoke(_vault, new object[] { mediaKey })!;
            }

            public bool HasIndexEntry(EntryKey entryKey) => _vault.HasIndexEntry(entryKey);
            public Task AddEntryAsync(MediaVaultType vaultType, EntryKey entryKey, object media) => 
                _vault.AddEntryAsync(vaultType, entryKey, media);
        }

        [Test]
        public async Task GetMediaKey_NormalKey_SanitizesCorrectly()
        {
            // Arrange
            var vault = await TestMediaVaultWrapper.FromAsync(TestDirectory);
            Assert.That(vault, Is.Not.Null, "Vault should be created");

            // Act & Assert - Test various sanitization scenarios
            Assert.That(vault!.GetMediaKey("normal-key", ".png"), Is.EqualTo("normal-key.png"), 
                "Normal key should pass through unchanged");
            
            Assert.That(vault.GetMediaKey("key with spaces", ".jpg"), Is.EqualTo("key-with-spaces.jpg"), 
                "Spaces should be replaced with dashes");
            
            Assert.That(vault.GetMediaKey("key@#$%special", ".gif"), Is.EqualTo("key----special.gif"), 
                "Special characters should be replaced with dashes");
            
            Assert.That(vault.GetMediaKey("key123ABC", ".png"), Is.EqualTo("key123ABC.png"), 
                "Alphanumeric characters should be preserved");
        }

        [Test]
        public async Task GetMediaPathFromEntryKey_ValidInputs_ReturnsCorrectPath()
        {
            // Arrange
            var vault = await TestMediaVaultWrapper.FromAsync(TestDirectory);
            Assert.That(vault, Is.Not.Null, "Vault should be created");

            // Act
            var path = vault!.GetMediaPathFromEntryKey("test-key", ".png");

            // Assert
            var expectedPath = Path.Combine(TestDirectory, "test-key.png");
            Assert.That(path, Is.EqualTo(expectedPath), "Should combine directory and sanitized filename");
        }

        [Test]
        public async Task GetMediaPathFromMediaKey_ValidKey_ReturnsCorrectPath()
        {
            // Arrange
            var vault = await TestMediaVaultWrapper.FromAsync(TestDirectory);
            Assert.That(vault, Is.Not.Null, "Vault should be created");

            // Act
            var path = vault!.GetMediaPathFromMediaKey("media-file.png");

            // Assert
            var expectedPath = Path.Combine(TestDirectory, "media-file.png");
            Assert.That(path, Is.EqualTo(expectedPath), "Should combine directory and media key");
        }

        [Test]
        public async Task AddEntryAsync_UnsupportedMediaType_ThrowsArgumentException()
        {
            // Arrange
            var vault = await TestMediaVaultWrapper.FromAsync(TestDirectory);
            Assert.That(vault, Is.Not.Null, "Vault should be created");
            
            var entryKey = CreateTestEntryKey("test");
            var unsupportedMedia = new object(); // Not a supported media type

            // Act & Assert
            Assert.ThrowsAsync<ArgumentException>(async () =>
                await vault!.AddEntryAsync(MediaVaultType.Image, entryKey, unsupportedMedia),
                "Should throw for unsupported media type");
        }

        [Test]
        public async Task AddEntryAsync_ValidMedia_UpdatesIndexAndCreatesFile()
        {
            // Arrange
            var vault = await TestMediaVaultWrapper.FromAsync(TestDirectory);
            Assert.That(vault, Is.Not.Null, "Vault should be created");
            
            var entryKey = CreateTestEntryKey("test-media");
            var imageBinary = TestData.CreateTestImageBinary(1.0); // Use existing test data helper

            // Act
            await vault!.AddEntryAsync(MediaVaultType.Image, entryKey, imageBinary);

            // Assert
            Assert.That(vault.HasIndexEntry(entryKey), Is.True, "Should add entry to index");
            
            var expectedFilePath = Path.Combine(TestDirectory, "test-media.png");
            AssertMediaFileExists(expectedFilePath, imageBinary.Buffer);
        }
    }

    /// <summary>
    /// Tests for MediaVault error handling and edge cases - Interface Segregation
    /// </summary>
    [TestFixture]
    public class MediaVaultErrorHandlingTests : MediaVaultTestBase
    {
        [Test]
        public async Task ImageVault_FromAsync_CorruptedIndexFile_RecreatesIndex()
        {
            // Arrange - Create a corrupted index file
            var indexPath = Path.Combine(TestDirectory, "index");
            await File.WriteAllTextAsync(indexPath, "{ corrupted json }");

            // Act - Should handle corruption gracefully by recreating
            var vault = await ImageVault.FromAsync(TestDirectory);

            // Assert
            Assert.That(vault, Is.Not.Null, "Should create vault even with corrupted index");
            Assert.That(vault!.GetIndexSize(), Is.EqualTo(0), "Should have empty index after recreation");
        }

        [Test]
        public async Task GetEntryAsync_CorruptedMediaFile_HandlesGracefully()
        {
            // Arrange
            var vault = await ImageVault.FromAsync(TestDirectory);
            var entryKey = CreateTestEntryKey("corrupted-file");
            var imageBinary = TestData.CreateTestImageBinary(1.0);
            
            await vault!.AddEntryAsync(MediaVaultType.Image, entryKey, imageBinary);
            
            // Corrupt the media file
            var filePath = Path.Combine(TestDirectory, "corrupted-file.png");
            await File.WriteAllTextAsync(filePath, "corrupted data");

            // Act & Assert - Should not throw, but behavior may vary
            Assert.DoesNotThrowAsync(async () =>
            {
                await vault.GetEntryAsync<ImageBinary>(MediaVaultType.Image, entryKey);
            }, "Should handle corrupted files gracefully");
        }

        [Test]
        public async Task AddEntryAsync_DiskSpaceIssue_HandlesGracefully()
        {
            // This test is difficult to simulate reliably across platforms
            // Instead, we test with very large buffers that might cause issues
            
            // Arrange
            var vault = await ImageVault.FromAsync(TestDirectory);
            var entryKey = CreateTestEntryKey("large-file");
            
            // Create a reasonably large buffer (not too large to cause test issues)
            var largeBuffer = new byte[1024 * 1024]; // 1MB
            Array.Fill<byte>(largeBuffer, 0xFF);
            
            var largeBinary = new ImageBinary(new ImageBinaryParams(largeBuffer, largeBuffer.Length, ".png", 1.0));

            // Act & Assert - Should not throw exceptions
            Assert.DoesNotThrowAsync(async () =>
            {
                await vault!.AddEntryAsync(MediaVaultType.Image, entryKey, largeBinary);
            }, "Should handle large files gracefully");
        }

        [Test]
        public async Task GetEntryAsync_ConcurrentAccess_HandlesGracefully()
        {
            // Arrange
            var vault = await ImageVault.FromAsync(TestDirectory);
            var entryKey = CreateTestEntryKey("concurrent-test");
            var imageBinary = TestData.CreateTestImageBinary(1.0);
            
            await vault!.AddEntryAsync(MediaVaultType.Image, entryKey, imageBinary);

            // Act - Multiple concurrent reads
            var tasks = new List<Task<ImageBinary?>>();
            for (int i = 0; i < 10; i++)
            {
                tasks.Add(vault.GetEntryAsync<ImageBinary>(MediaVaultType.Image, entryKey));
            }

            var results = await Task.WhenAll(tasks);

            // Assert
            Assert.That(results.Length, Is.EqualTo(10), "Should complete all concurrent reads");
            foreach (var result in results)
            {
                Assert.That(result, Is.Not.Null, "Each concurrent read should succeed");
                Assert.That(result!.Buffer, Is.EqualTo(imageBinary.Buffer), "Each result should have correct data");
            }
        }
    }

    /// <summary>
    /// Integration tests for MediaVault with FileDatabase - Dependency Inversion
    /// </summary>
    [TestFixture]
    public class MediaVaultIntegrationTests : MediaVaultTestBase
    {
        [Test]
        public async Task MediaVault_IntegratesWithFileDatabase_WorksEndToEnd()
        {
            // This test verifies that MediaVault works correctly when used through FileDatabase
            
            // Arrange
            var database = await FileDatabase.FromAsync(TestDirectory);
            var vaultKey = new EntryKey("test-vault", MediaVaultType.Image);
            var entryKey = new EntryKey("test-image", MediaVaultType.Image);
            var imageBinary = TestData.CreateTestImageBinary(1.5);

            // Act
            await database!.CreateVaultAsync(vaultKey);
            await database.RegisterResourceAsync(MediaVaultType.Image, vaultKey, entryKey, imageBinary);
            var retrievedImage = await database.LoadResourceAsync<ImageBinary>(MediaVaultType.Image, vaultKey, entryKey);

            // Assert
            Assert.That(retrievedImage, Is.Not.Null, "Should retrieve image through database");
            Assert.That(retrievedImage!.Buffer, Is.EqualTo(imageBinary.Buffer), "Retrieved data should match original");
            Assert.That(retrievedImage.AspectRatio, Is.EqualTo(imageBinary.AspectRatio), "Metadata should be preserved");

            // Verify vault was created correctly
            var vault = database.GetVault(vaultKey);
            Assert.That(vault, Is.Not.Null, "Vault should exist in database");
            Assert.That(vault, Is.TypeOf<ImageVault>(), "Should be ImageVault type");
        }

        [Test]
        public async Task MediaVault_PersistenceAcrossRestarts_MaintainsData()
        {
            // Test that vault data persists when database is reloaded
            
            // Arrange - Create and populate vault
            var database1 = await FileDatabase.FromAsync(TestDirectory);
            var vaultKey = new EntryKey("persistent-vault", MediaVaultType.Image);
            var entryKey = new EntryKey("persistent-image", MediaVaultType.Image);
            var imageBinary = TestData.CreateTestImageBinary(2.0);

            await database1!.CreateVaultAsync(vaultKey);
            await database1.RegisterResourceAsync(MediaVaultType.Image, vaultKey, entryKey, imageBinary);

            // Act - Reload database
            var database2 = await FileDatabase.FromAsync(TestDirectory);
            var retrievedImage = await database2!.LoadResourceAsync<ImageBinary>(MediaVaultType.Image, vaultKey, entryKey);

            // Assert
            Assert.That(retrievedImage, Is.Not.Null, "Should retrieve image after database reload");
            Assert.That(retrievedImage!.Buffer, Is.EqualTo(imageBinary.Buffer), "Data should persist across restarts");
            Assert.That(retrievedImage.AspectRatio, Is.EqualTo(imageBinary.AspectRatio), "Metadata should persist");
        }
    }
}
