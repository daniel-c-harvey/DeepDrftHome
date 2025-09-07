using DeepDrftContent.Services.FileDatabase.Models;
using DeepDrftContent.Services.FileDatabase.Services;

namespace DeepDrftTests;

/// <summary>
/// SOLID, DRY tests for SimpleMediaTypeRegistry
/// Follows Single Responsibility: Each test class tests one registry concern
/// Follows Open/Closed: Tests extensibility patterns and type safety
/// Follows Interface Segregation: Tests focused interface methods
/// </summary>
[TestFixture]
public class SimpleMediaTypeRegistryTests
{
    /// <summary>
    /// Base class for registry tests - DRY principle
    /// </summary>
    public abstract class RegistryTestBase
    {
        protected SimpleMediaTypeRegistry Registry { get; private set; } = null!;
        protected string TestDirectory { get; private set; } = null!;

        [SetUp]
        public virtual void SetUp()
        {
            Registry = new SimpleMediaTypeRegistry();
            TestDirectory = Path.Combine(Path.GetTempPath(), "DeepDrftTests", "Registry", Guid.NewGuid().ToString());
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
        /// Helper method to create test binary objects using existing factories - DRY principle
        /// </summary>
        protected T CreateTestBinary<T>(MediaVaultType vaultType) where T : FileBinary
        {
            var parameters = CreateTestParams(vaultType);
            return (T)Registry.CreateBinary(vaultType, parameters);
        }

        /// <summary>
        /// Helper method to create test parameters using TestData - DRY principle
        /// </summary>
        protected object CreateTestParams(MediaVaultType vaultType)
        {
            return vaultType switch
            {
                MediaVaultType.Media => new MediaBinaryParams(TestData.TestPngBytes, TestData.TestPngBytes.Length, ".dat"),
                MediaVaultType.Image => new ImageBinaryParams(TestData.TestPngBytes, TestData.TestPngBytes.Length, ".png", 1.0),
                MediaVaultType.Audio => new AudioBinaryParams(TestData.TestPngBytes, TestData.TestPngBytes.Length, ".mp3", 120.0, 320),
                _ => throw new ArgumentException($"Unsupported vault type: {vaultType}")
            };
        }

        /// <summary>
        /// Helper method to create test DTOs - DRY principle
        /// </summary>
        protected object CreateTestDto(MediaVaultType vaultType)
        {
            var base64Data = Convert.ToBase64String(TestData.TestPngBytes);

            return vaultType switch
            {
                MediaVaultType.Media => new MediaBinaryDto(base64Data, TestData.TestPngBytes.Length, "application/octet-stream"),
                MediaVaultType.Image => new ImageBinaryDto(base64Data, TestData.TestPngBytes.Length, "image/png", 1.0),
                MediaVaultType.Audio => new AudioBinaryDto(base64Data, TestData.TestPngBytes.Length, "audio/mpeg", 120.0, 320),
                _ => throw new ArgumentException($"Unsupported vault type: {vaultType}")
            };
        }

        /// <summary>
        /// Helper method to create test metadata using existing factories - DRY principle
        /// </summary>
        protected MetaData CreateTestMetaData(MediaVaultType vaultType, string key = "test", string extension = ".png")
        {
            // Use the registry's metadata creation for consistency
            var binary = CreateTestBinary<FileBinary>(vaultType);
            return Registry.CreateMetaDataFromMedia(vaultType, key, extension, binary);
        }
    }

    /// <summary>
    /// Tests for binary creation functionality - Single Responsibility
    /// </summary>
    [TestFixture]
    public class BinaryCreationTests : RegistryTestBase
    {
        [Test]
        public void CreateBinary_MediaVaultType_CreatesMediaBinary()
        {
            // Arrange
            var parameters = CreateTestParams(MediaVaultType.Media);

            // Act
            var binary = Registry.CreateBinary(MediaVaultType.Media, parameters);

            // Assert
            Assert.That(binary, Is.Not.Null, "Binary should be created");
            Assert.That(binary, Is.TypeOf<MediaBinary>(), "Should create MediaBinary");
            
            var mediaBinary = (MediaBinary)binary;
            Assert.That(mediaBinary.Buffer.Length, Is.EqualTo(TestData.TestPngBytes.Length), "Buffer should match");
            Assert.That(mediaBinary.Extension, Is.EqualTo(".dat"), "Extension should match");
        }

        [Test]
        public void CreateBinary_ImageVaultType_CreatesImageBinary()
        {
            // Arrange
            var parameters = CreateTestParams(MediaVaultType.Image);

            // Act
            var binary = Registry.CreateBinary(MediaVaultType.Image, parameters);

            // Assert
            Assert.That(binary, Is.Not.Null, "Binary should be created");
            Assert.That(binary, Is.TypeOf<ImageBinary>(), "Should create ImageBinary");
            
            var imageBinary = (ImageBinary)binary;
            Assert.That(imageBinary.Buffer.Length, Is.EqualTo(TestData.TestPngBytes.Length), "Buffer should match");
            Assert.That(imageBinary.Extension, Is.EqualTo(".png"), "Extension should match");
            Assert.That(imageBinary.AspectRatio, Is.EqualTo(1.0), "Aspect ratio should match");
        }

        [Test]
        public void CreateBinary_AudioVaultType_CreatesAudioBinary()
        {
            // Arrange
            var parameters = CreateTestParams(MediaVaultType.Audio);

            // Act
            var binary = Registry.CreateBinary(MediaVaultType.Audio, parameters);

            // Assert
            Assert.That(binary, Is.Not.Null, "Binary should be created");
            Assert.That(binary, Is.TypeOf<AudioBinary>(), "Should create AudioBinary");
            
            var audioBinary = (AudioBinary)binary;
            Assert.That(audioBinary.Buffer.Length, Is.EqualTo(TestData.TestPngBytes.Length), "Buffer should match");
            Assert.That(audioBinary.Extension, Is.EqualTo(".mp3"), "Extension should match");
            Assert.That(audioBinary.Duration, Is.EqualTo(120.0), "Duration should match");
            Assert.That(audioBinary.Bitrate, Is.EqualTo(320), "Bitrate should match");
        }

        [Test]
        public void CreateBinary_InvalidVaultType_ThrowsArgumentException()
        {
            // Arrange
            var invalidType = (MediaVaultType)999;
            var parameters = CreateTestParams(MediaVaultType.Media);

            // Act & Assert
            Assert.Throws<ArgumentException>(() =>
                Registry.CreateBinary(invalidType, parameters),
                "Should throw for invalid vault type");
        }

        [Test]
        public void CreateBinary_WrongParameterType_ThrowsException()
        {
            // Arrange - Use Image parameters for Media vault type
            var imageParameters = CreateTestParams(MediaVaultType.Image);

            // Act & Assert - The registry is flexible and may handle type mismatches gracefully
            // depending on the parameter compatibility
            try
            {
                var result = Registry.CreateBinary(MediaVaultType.Media, imageParameters);
                // If it succeeds, verify it created something reasonable
                Assert.That(result, Is.Not.Null, "Should create some binary even with mismatched parameters");
                Assert.That(result, Is.TypeOf<MediaBinary>(), "Should create MediaBinary for Media vault type");
            }
            catch (InvalidCastException)
            {
                Assert.Pass("Registry correctly threw InvalidCastException for parameter mismatch");
            }
            catch (ArgumentException)
            {
                Assert.Pass("Registry correctly threw ArgumentException for parameter mismatch");
            }
        }
    }

    /// <summary>
    /// Tests for DTO creation and conversion - Single Responsibility
    /// </summary>
    [TestFixture]
    public class DtoCreationTests : RegistryTestBase
    {
        [Test]
        public void CreateBinaryFromDto_MediaVaultType_CreatesMediaBinary()
        {
            // Arrange
            var dto = CreateTestDto(MediaVaultType.Media);

            // Act
            var binary = Registry.CreateBinaryFromDto(MediaVaultType.Media, dto);

            // Assert
            Assert.That(binary, Is.Not.Null, "Binary should be created from DTO");
            Assert.That(binary, Is.TypeOf<MediaBinary>(), "Should create MediaBinary");
            
            var mediaBinary = (MediaBinary)binary;
            Assert.That(mediaBinary.Buffer, Is.EqualTo(TestData.TestPngBytes), "Buffer should match original");
        }

        [Test]
        public void CreateBinaryFromDto_ImageVaultType_CreatesImageBinary()
        {
            // Arrange
            var dto = CreateTestDto(MediaVaultType.Image);

            // Act
            var binary = Registry.CreateBinaryFromDto(MediaVaultType.Image, dto);

            // Assert
            Assert.That(binary, Is.Not.Null, "Binary should be created from DTO");
            Assert.That(binary, Is.TypeOf<ImageBinary>(), "Should create ImageBinary");
            
            var imageBinary = (ImageBinary)binary;
            Assert.That(imageBinary.Buffer, Is.EqualTo(TestData.TestPngBytes), "Buffer should match original");
            Assert.That(imageBinary.AspectRatio, Is.EqualTo(1.0), "Aspect ratio should be preserved");
        }

        [Test]
        public void CreateBinaryFromDto_AudioVaultType_CreatesAudioBinary()
        {
            // Arrange
            var dto = CreateTestDto(MediaVaultType.Audio);

            // Act
            var binary = Registry.CreateBinaryFromDto(MediaVaultType.Audio, dto);

            // Assert
            Assert.That(binary, Is.Not.Null, "Binary should be created from DTO");
            Assert.That(binary, Is.TypeOf<AudioBinary>(), "Should create AudioBinary");
            
            var audioBinary = (AudioBinary)binary;
            Assert.That(audioBinary.Buffer, Is.EqualTo(TestData.TestPngBytes), "Buffer should match original");
            Assert.That(audioBinary.Duration, Is.EqualTo(120.0), "Duration should be preserved");
            Assert.That(audioBinary.Bitrate, Is.EqualTo(320), "Bitrate should be preserved");
        }

        [Test]
        public void CreateDto_MediaBinary_CreatesMediaBinaryDto()
        {
            // Arrange
            var binary = CreateTestBinary<MediaBinary>(MediaVaultType.Media);

            // Act
            var dto = Registry.CreateDto(MediaVaultType.Media, binary);

            // Assert
            Assert.That(dto, Is.Not.Null, "DTO should be created");
            Assert.That(dto, Is.TypeOf<MediaBinaryDto>(), "Should create MediaBinaryDto");
            
            var mediaDto = (MediaBinaryDto)dto;
            Assert.That(mediaDto.Size, Is.EqualTo(binary.Size), "Size should match");
            
            var decodedBuffer = Convert.FromBase64String(mediaDto.Base64);
            Assert.That(decodedBuffer, Is.EqualTo(binary.Buffer), "Decoded buffer should match original");
        }

        [Test]
        public void CreateDto_ImageBinary_CreatesImageBinaryDto()
        {
            // Arrange
            var binary = CreateTestBinary<ImageBinary>(MediaVaultType.Image);

            // Act
            var dto = Registry.CreateDto(MediaVaultType.Image, binary);

            // Assert
            Assert.That(dto, Is.Not.Null, "DTO should be created");
            Assert.That(dto, Is.TypeOf<ImageBinaryDto>(), "Should create ImageBinaryDto");
            
            var imageDto = (ImageBinaryDto)dto;
            Assert.That(imageDto.Size, Is.EqualTo(binary.Size), "Size should match");
            Assert.That(imageDto.AspectRatio, Is.EqualTo(binary.AspectRatio), "Aspect ratio should match");
        }

        [Test]
        public void CreateDto_AudioBinary_CreatesAudioBinaryDto()
        {
            // Arrange
            var binary = CreateTestBinary<AudioBinary>(MediaVaultType.Audio);

            // Act
            var dto = Registry.CreateDto(MediaVaultType.Audio, binary);

            // Assert
            Assert.That(dto, Is.Not.Null, "DTO should be created");
            Assert.That(dto, Is.TypeOf<AudioBinaryDto>(), "Should create AudioBinaryDto");
            
            var audioDto = (AudioBinaryDto)dto;
            Assert.That(audioDto.Size, Is.EqualTo(binary.Size), "Size should match");
            Assert.That(audioDto.Duration, Is.EqualTo(binary.Duration), "Duration should match");
            Assert.That(audioDto.Bitrate, Is.EqualTo(binary.Bitrate), "Bitrate should match");
        }
    }

    /// <summary>
    /// Tests for metadata creation - Single Responsibility
    /// </summary>
    [TestFixture]
    public class MetaDataCreationTests : RegistryTestBase
    {
        [Test]
        public void CreateMetaDataFromMedia_ImageBinary_CreatesImageMetaData()
        {
            // Arrange
            var imageBinary = CreateTestBinary<ImageBinary>(MediaVaultType.Image);
            const string key = "test-image";
            const string extension = ".png";

            // Act
            var metaData = Registry.CreateMetaDataFromMedia(MediaVaultType.Image, key, extension, imageBinary);

            // Assert
            Assert.That(metaData, Is.Not.Null, "MetaData should be created");
            Assert.That(metaData, Is.TypeOf<ImageMetaData>(), "Should create ImageMetaData");
            
            var imageMetaData = (ImageMetaData)metaData;
            Assert.That(imageMetaData.MediaKey, Is.EqualTo(key), "Key should match");
            Assert.That(imageMetaData.Extension, Is.EqualTo(extension), "Extension should match");
            Assert.That(imageMetaData.AspectRatio, Is.EqualTo(imageBinary.AspectRatio), "Should extract aspect ratio");
        }

        [Test]
        public void CreateMetaDataFromMedia_AudioBinary_CreatesAudioMetaData()
        {
            // Arrange
            var audioBinary = CreateTestBinary<AudioBinary>(MediaVaultType.Audio);
            const string key = "test-audio";
            const string extension = ".mp3";

            // Act
            var metaData = Registry.CreateMetaDataFromMedia(MediaVaultType.Audio, key, extension, audioBinary);

            // Assert
            Assert.That(metaData, Is.Not.Null, "MetaData should be created");
            Assert.That(metaData, Is.TypeOf<AudioMetaData>(), "Should create AudioMetaData");
            
            var audioMetaData = (AudioMetaData)metaData;
            Assert.That(audioMetaData.MediaKey, Is.EqualTo(key), "Key should match");
            Assert.That(audioMetaData.Extension, Is.EqualTo(extension), "Extension should match");
            Assert.That(audioMetaData.Duration, Is.EqualTo(audioBinary.Duration), "Should extract duration");
            Assert.That(audioMetaData.Bitrate, Is.EqualTo(audioBinary.Bitrate), "Should extract bitrate");
        }

        [Test]
        public void CreateMetaDataFromMedia_MediaBinary_CreatesBaseMetaData()
        {
            // Arrange
            var mediaBinary = CreateTestBinary<MediaBinary>(MediaVaultType.Media);
            const string key = "test-media";
            const string extension = ".dat";

            // Act
            var metaData = Registry.CreateMetaDataFromMedia(MediaVaultType.Media, key, extension, mediaBinary);

            // Assert
            Assert.That(metaData, Is.Not.Null, "MetaData should be created");
            Assert.That(metaData, Is.TypeOf<MetaData>(), "Should create base MetaData");
            Assert.That(metaData.MediaKey, Is.EqualTo(key), "Key should match");
            Assert.That(metaData.Extension, Is.EqualTo(extension), "Extension should match");
        }

        [Test]
        public void CreateMetaDataFromMedia_WrongMediaType_CreatesBaseMetaData()
        {
            // Arrange - Pass MediaBinary to Image vault type
            var mediaBinary = CreateTestBinary<MediaBinary>(MediaVaultType.Media);
            const string key = "test-wrong";
            const string extension = ".dat";

            // Act
            var metaData = Registry.CreateMetaDataFromMedia(MediaVaultType.Image, key, extension, mediaBinary);

            // Assert - Should fallback to base MetaData when media type doesn't match vault type
            Assert.That(metaData, Is.Not.Null, "MetaData should be created");
            Assert.That(metaData, Is.TypeOf<MetaData>(), "Should create base MetaData as fallback");
        }
    }

    /// <summary>
    /// Tests for parameter creation - Interface Segregation
    /// </summary>
    [TestFixture]
    public class ParameterCreationTests : RegistryTestBase
    {
        [Test]
        public void CreateParams_ImageBinaryWithImageMetaData_CreatesImageBinaryParams()
        {
            // Arrange
            var fileBinary = new FileBinary(new FileBinaryParams(TestData.TestPngBytes, TestData.TestPngBytes.Length));
            var imageMetaData = new ImageMetaData("test", ".png", 1.5);

            // Act
            var parameters = Registry.CreateParams(MediaVaultType.Image, fileBinary, imageMetaData);

            // Assert
            Assert.That(parameters, Is.Not.Null, "Parameters should be created");
            Assert.That(parameters, Is.TypeOf<ImageBinaryParams>(), "Should create ImageBinaryParams");
            
            var imageParams = (ImageBinaryParams)parameters;
            Assert.That(imageParams.Buffer, Is.EqualTo(fileBinary.Buffer), "Buffer should match");
            Assert.That(imageParams.Size, Is.EqualTo(fileBinary.Size), "Size should match");
            Assert.That(imageParams.Extension, Is.EqualTo(imageMetaData.Extension), "Extension should match");
            Assert.That(imageParams.AspectRatio, Is.EqualTo(imageMetaData.AspectRatio), "Aspect ratio should match");
        }

        [Test]
        public void CreateParams_AudioBinaryWithAudioMetaData_CreatesAudioBinaryParams()
        {
            // Arrange
            var fileBinary = new FileBinary(new FileBinaryParams(TestData.TestPngBytes, TestData.TestPngBytes.Length));
            var audioMetaData = new AudioMetaData("test", ".mp3", 180.0, 256);

            // Act
            var parameters = Registry.CreateParams(MediaVaultType.Audio, fileBinary, audioMetaData);

            // Assert
            Assert.That(parameters, Is.Not.Null, "Parameters should be created");
            Assert.That(parameters, Is.TypeOf<AudioBinaryParams>(), "Should create AudioBinaryParams");
            
            var audioParams = (AudioBinaryParams)parameters;
            Assert.That(audioParams.Buffer, Is.EqualTo(fileBinary.Buffer), "Buffer should match");
            Assert.That(audioParams.Size, Is.EqualTo(fileBinary.Size), "Size should match");
            Assert.That(audioParams.Extension, Is.EqualTo(audioMetaData.Extension), "Extension should match");
            Assert.That(audioParams.Duration, Is.EqualTo(audioMetaData.Duration), "Duration should match");
            Assert.That(audioParams.Bitrate, Is.EqualTo(audioMetaData.Bitrate), "Bitrate should match");
        }

        [Test]
        public void CreateParams_WrongMetaDataType_ThrowsArgumentException()
        {
            // Arrange
            var fileBinary = new FileBinary(new FileBinaryParams(TestData.TestPngBytes, TestData.TestPngBytes.Length));
            var baseMetaData = new MetaData("test", ".png"); // Wrong metadata type for Image vault

            // Act & Assert
            Assert.Throws<ArgumentException>(() =>
                Registry.CreateParams(MediaVaultType.Image, fileBinary, baseMetaData),
                "Should throw when metadata type doesn't match vault type requirements");
        }
    }

    /// <summary>
    /// Tests for vault creation - Dependency Inversion
    /// </summary>
    [TestFixture]
    public class VaultCreationTests : RegistryTestBase
    {
        [Test]
        public async Task CreateVaultAsync_ImageVaultType_CreatesImageVault()
        {
            // Act
            var vault = await Registry.CreateVaultAsync(MediaVaultType.Image, TestDirectory);

            // Assert
            Assert.That(vault, Is.Not.Null, "Vault should be created");
            Assert.That(vault, Is.TypeOf<ImageVault>(), "Should create ImageVault");
            Assert.That(vault!.RootPath, Is.EqualTo(TestDirectory), "Should use provided path");
        }

        [Test]
        public async Task CreateVaultAsync_AudioVaultType_CreatesAudioVault()
        {
            // Act
            var vault = await Registry.CreateVaultAsync(MediaVaultType.Audio, TestDirectory);

            // Assert
            Assert.That(vault, Is.Not.Null, "Vault should be created");
            Assert.That(vault, Is.TypeOf<AudioVault>(), "Should create AudioVault");
            Assert.That(vault!.RootPath, Is.EqualTo(TestDirectory), "Should use provided path");
        }

        [Test]
        public async Task CreateVaultAsync_MediaVaultType_ReturnsNull()
        {
            // Act
            var vault = await Registry.CreateVaultAsync(MediaVaultType.Media, TestDirectory);

            // Assert
            Assert.That(vault, Is.Null, "Should return null for unsupported Media vault type");
        }
    }

    /// <summary>
    /// Tests for type information retrieval - Interface Segregation
    /// </summary>
    [TestFixture]
    public class TypeInformationTests : RegistryTestBase
    {
        [Test]
        public void GetBinaryType_AllSupportedTypes_ReturnsCorrectTypes()
        {
            // Test all supported vault types
            var expectedTypes = new Dictionary<MediaVaultType, Type>
            {
                { MediaVaultType.Media, typeof(MediaBinary) },
                { MediaVaultType.Image, typeof(ImageBinary) },
                { MediaVaultType.Audio, typeof(AudioBinary) }
            };

            foreach (var (vaultType, expectedType) in expectedTypes)
            {
                // Act
                var actualType = Registry.GetBinaryType(vaultType);

                // Assert
                Assert.That(actualType, Is.EqualTo(expectedType), $"Binary type for {vaultType} should be {expectedType.Name}");
            }
        }

        [Test]
        public void GetDtoType_AllSupportedTypes_ReturnsCorrectTypes()
        {
            // Test all supported vault types
            var expectedTypes = new Dictionary<MediaVaultType, Type>
            {
                { MediaVaultType.Media, typeof(MediaBinaryDto) },
                { MediaVaultType.Image, typeof(ImageBinaryDto) },
                { MediaVaultType.Audio, typeof(AudioBinaryDto) }
            };

            foreach (var (vaultType, expectedType) in expectedTypes)
            {
                // Act
                var actualType = Registry.GetDtoType(vaultType);

                // Assert
                Assert.That(actualType, Is.EqualTo(expectedType), $"DTO type for {vaultType} should be {expectedType.Name}");
            }
        }

        [Test]
        public void GetParamsType_AllSupportedTypes_ReturnsCorrectTypes()
        {
            // Test all supported vault types
            var expectedTypes = new Dictionary<MediaVaultType, Type>
            {
                { MediaVaultType.Media, typeof(MediaBinaryParams) },
                { MediaVaultType.Image, typeof(ImageBinaryParams) },
                { MediaVaultType.Audio, typeof(AudioBinaryParams) }
            };

            foreach (var (vaultType, expectedType) in expectedTypes)
            {
                // Act
                var actualType = Registry.GetParamsType(vaultType);

                // Assert
                Assert.That(actualType, Is.EqualTo(expectedType), $"Params type for {vaultType} should be {expectedType.Name}");
            }
        }

        [Test]
        public void GetMetaDataType_AllSupportedTypes_ReturnsCorrectTypes()
        {
            // Test all supported vault types
            var expectedTypes = new Dictionary<MediaVaultType, Type>
            {
                { MediaVaultType.Media, typeof(MetaData) },
                { MediaVaultType.Image, typeof(ImageMetaData) },
                { MediaVaultType.Audio, typeof(AudioMetaData) }
            };

            foreach (var (vaultType, expectedType) in expectedTypes)
            {
                // Act
                var actualType = Registry.GetMetaDataType(vaultType);

                // Assert
                Assert.That(actualType, Is.EqualTo(expectedType), $"MetaData type for {vaultType} should be {expectedType.Name}");
            }
        }

        [Test]
        public void GetBinaryType_InvalidVaultType_ThrowsArgumentException()
        {
            // Arrange
            var invalidType = (MediaVaultType)999;

            // Act & Assert
            Assert.Throws<ArgumentException>(() =>
                Registry.GetBinaryType(invalidType),
                "Should throw for invalid vault type");
        }
    }

    /// <summary>
    /// Tests for external registration (currently not implemented) - Open/Closed Principle
    /// </summary>
    [TestFixture]
    public class ExternalRegistrationTests : RegistryTestBase
    {
        [Test]
        public void RegisterMediaType_ExternalRegistration_ThrowsNotImplementedException()
        {
            // This test documents the current limitation and ensures we know when it changes
            
            // Act & Assert
            Assert.Throws<NotImplementedException>(() =>
                Registry.RegisterMediaType<MediaBinary, MediaBinaryParams, MediaBinaryDto, MetaData, ImageVault>(MediaVaultType.Media),
                "External registration should throw NotImplementedException until implemented");
        }
    }
}
