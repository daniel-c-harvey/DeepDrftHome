using DeepDrftContent.FileDatabase.Models;

namespace DeepDrftTests;

/// <summary>
/// Tests for model classes and data structures
/// </summary>
[TestFixture]
public class ModelTests
{
    [TestFixture]
    public class EntryKeyTests
    {
        [Test]
        public void EntryKey_CanBeCreated()
        {
            // Arrange
            var key = "test-key";
            var type = MediaVaultType.Image;

            // Act
            var entryKey = new EntryKey(key, type);

            // Assert
            Assert.That(entryKey.Key, Is.EqualTo(key), "Key should match");
            Assert.That(entryKey.Type, Is.EqualTo(type), "Type should match");
        }

        [Test]
        public void EntryKey_SupportsStructuralEquality()
        {
            // Arrange
            var key1 = new EntryKey("test", MediaVaultType.Image);
            var key2 = new EntryKey("test", MediaVaultType.Image);
            var key3 = new EntryKey("different", MediaVaultType.Image);

            // Act & Assert
            Assert.That(key1, Is.EqualTo(key2), "Structurally equal keys should be equal");
            Assert.That(key1, Is.Not.EqualTo(key3), "Different keys should not be equal");
            Assert.That(key1.GetHashCode(), Is.EqualTo(key2.GetHashCode()), "Equal keys should have same hash code");
        }
    }

    [TestFixture]
    public class MediaModelTests
    {
        [Test]
        public void FileBinary_CanBeCreated()
        {
            // Arrange
            var buffer = TestData.TestPngBytes;
            var size = buffer.Length;
            var parameters = new FileBinaryParams(buffer, size);

            // Act
            var fileBinary = new FileBinary(parameters);

            // Assert
            Assert.That(fileBinary.Buffer, Is.EqualTo(buffer), "Buffer should match");
            Assert.That(fileBinary.Size, Is.EqualTo(size), "Size should match");
        }

        [Test]
        public void FileBinary_CanBeCreatedFromDto()
        {
            // Arrange
            var originalBuffer = TestData.TestPngBytes;
            var base64Data = Convert.ToBase64String(originalBuffer);
            var dto = new FileBinaryDto(base64Data, originalBuffer.Length);

            // Act
            var fileBinary = FileBinary.From(dto);

            // Assert
            Assert.That(fileBinary.Size, Is.EqualTo(originalBuffer.Length), "Size should match");
            Assert.That(fileBinary.Buffer, Is.EqualTo(originalBuffer), "Buffer should match original");
        }

        [Test]
        public void MediaBinary_CanBeCreated()
        {
            // Arrange
            var buffer = TestData.TestPngBytes;
            var size = buffer.Length;
            var extension = ".png";
            var parameters = new MediaBinaryParams(buffer, size, extension);

            // Act
            var mediaBinary = new MediaBinary(parameters);

            // Assert
            Assert.That(mediaBinary.Buffer, Is.EqualTo(buffer), "Buffer should match");
            Assert.That(mediaBinary.Size, Is.EqualTo(size), "Size should match");
            Assert.That(mediaBinary.Extension, Is.EqualTo(extension), "Extension should match");
        }

        [Test]
        public void ImageBinary_CanBeCreated()
        {
            // Arrange
            var buffer = TestData.TestPngBytes;
            var size = buffer.Length;
            var extension = ".png";
            var aspectRatio = 1.5;
            var parameters = new ImageBinaryParams(buffer, size, extension, aspectRatio);

            // Act
            var imageBinary = new ImageBinary(parameters);

            // Assert
            Assert.That(imageBinary.Buffer, Is.EqualTo(buffer), "Buffer should match");
            Assert.That(imageBinary.Size, Is.EqualTo(size), "Size should match");
            Assert.That(imageBinary.Extension, Is.EqualTo(extension), "Extension should match");
            Assert.That(imageBinary.AspectRatio, Is.EqualTo(aspectRatio), "Aspect ratio should match");
        }

        [Test]
        public void ImageBinary_CanBeCreatedFromDto()
        {
            // Arrange
            var originalBuffer = TestData.TestPngBytes;
            var base64Data = Convert.ToBase64String(originalBuffer);
            var dto = new ImageBinaryDto(base64Data, originalBuffer.Length, "image/png", 1.0);

            // Act
            var imageBinary = ImageBinary.From(dto);

            // Assert
            Assert.That(imageBinary.Size, Is.EqualTo(originalBuffer.Length), "Size should match");
            Assert.That(imageBinary.Buffer, Is.EqualTo(originalBuffer), "Buffer should match original");
            Assert.That(imageBinary.Extension, Is.EqualTo(".png"), "Extension should match");
            Assert.That(imageBinary.AspectRatio, Is.EqualTo(1.0), "Aspect ratio should match");
        }

        [Test]
        public void ImageBinaryDto_CanBeCreatedFromImageBinary()
        {
            // Arrange
            var imageBinary = TestData.CreateTestImageBinary(1.5);

            // Act
            var dto = new ImageBinaryDto(imageBinary);

            // Assert
            Assert.That(dto.Size, Is.EqualTo(imageBinary.Size), "Size should match");
            Assert.That(dto.Mime, Is.EqualTo(MimeTypeExtensions.GetMimeType(imageBinary.Extension)), "MIME type should match");
            Assert.That(dto.AspectRatio, Is.EqualTo(imageBinary.AspectRatio), "Aspect ratio should match");
            
            // Verify base64 encoding
            var decodedBuffer = Convert.FromBase64String(dto.Base64);
            Assert.That(decodedBuffer, Is.EqualTo(imageBinary.Buffer), "Decoded buffer should match original");
        }

        [Test]
        public void AudioBinary_CanBeCreated()
        {
            // Arrange
            var buffer = TestData.TestPngBytes;
            var size = buffer.Length;
            var extension = ".mp3";
            var duration = 240.5;
            var bitrate = 192;
            var parameters = new AudioBinaryParams(buffer, size, extension, duration, bitrate);

            // Act
            var audioBinary = new AudioBinary(parameters);

            // Assert
            Assert.That(audioBinary.Buffer, Is.EqualTo(buffer), "Buffer should match");
            Assert.That(audioBinary.Size, Is.EqualTo(size), "Size should match");
            Assert.That(audioBinary.Extension, Is.EqualTo(extension), "Extension should match");
            Assert.That(audioBinary.Duration, Is.EqualTo(duration), "Duration should match");
            Assert.That(audioBinary.Bitrate, Is.EqualTo(bitrate), "Bitrate should match");
        }

        [Test]
        public void AudioBinary_CanBeCreatedFromDto()
        {
            // Arrange
            var originalBuffer = TestData.TestPngBytes;
            var base64Data = Convert.ToBase64String(originalBuffer);
            var duration = 180.0;
            var bitrate = 256;
            var dto = new AudioBinaryDto(base64Data, originalBuffer.Length, "audio/mpeg", duration, bitrate);

            // Act
            var audioBinary = AudioBinary.From(dto);

            // Assert
            Assert.That(audioBinary.Size, Is.EqualTo(originalBuffer.Length), "Size should match");
            Assert.That(audioBinary.Buffer, Is.EqualTo(originalBuffer), "Buffer should match original");
            Assert.That(audioBinary.Extension, Is.EqualTo(".mp3"), "Extension should match");
            Assert.That(audioBinary.Duration, Is.EqualTo(duration), "Duration should match");
            Assert.That(audioBinary.Bitrate, Is.EqualTo(bitrate), "Bitrate should match");
        }

        [Test]
        public void AudioBinaryDto_CanBeCreatedFromAudioBinary()
        {
            // Arrange
            var audioBinary = TestData.CreateTestAudioBinary(300.5, 128);

            // Act
            var dto = new AudioBinaryDto(audioBinary);

            // Assert
            Assert.That(dto.Size, Is.EqualTo(audioBinary.Size), "Size should match");
            Assert.That(dto.Mime, Is.EqualTo(MimeTypeExtensions.GetMimeType(audioBinary.Extension)), "MIME type should match");
            Assert.That(dto.Duration, Is.EqualTo(audioBinary.Duration), "Duration should match");
            Assert.That(dto.Bitrate, Is.EqualTo(audioBinary.Bitrate), "Bitrate should match");
            
            // Verify base64 encoding
            var decodedBuffer = Convert.FromBase64String(dto.Base64);
            Assert.That(decodedBuffer, Is.EqualTo(audioBinary.Buffer), "Decoded buffer should match original");
        }
    }

    [TestFixture]
    public class MetaDataTests
    {
        [Test]
        public void MetaData_CanBeCreated()
        {
            // Arrange
            var key = "test-key";
            var extension = ".png";

            // Act
            var metaData = new MetaData(key, extension);

            // Assert
            Assert.That(metaData.MediaKey, Is.EqualTo(key), "MediaKey should match");
            Assert.That(metaData.Extension, Is.EqualTo(extension), "Extension should match");
        }

        [Test]
        public void ImageMetaData_CanBeCreated()
        {
            // Arrange
            var key = "test-image";
            var extension = ".jpg";
            var aspectRatio = 1.77;

            // Act
            var imageMetaData = new ImageMetaData(key, extension, aspectRatio);

            // Assert
            Assert.That(imageMetaData.MediaKey, Is.EqualTo(key), "MediaKey should match");
            Assert.That(imageMetaData.Extension, Is.EqualTo(extension), "Extension should match");
            Assert.That(imageMetaData.AspectRatio, Is.EqualTo(aspectRatio), "Aspect ratio should match");
        }

        [Test]
        public void MetaDataFactory_CreatesMediaMetaData()
        {
            // Arrange
            var key = "test";
            var extension = ".png";

            // Act
            var mediaMetaData = MetaDataFactory.Create(MediaVaultType.Media, key, extension);

            // Assert
            Assert.That(mediaMetaData, Is.TypeOf<MetaData>(), "Should create MetaData for Media type");
            Assert.That(mediaMetaData.MediaKey, Is.EqualTo(key), "MediaKey should match");
            Assert.That(mediaMetaData.Extension, Is.EqualTo(extension), "Extension should match");
        }

        [Test]
        public void MetaDataFactory_CreatesImageMetaData()
        {
            // Arrange
            var key = "test-image";
            var extension = ".png";
            var aspectRatio = 2.0;

            // Act
            var imageMetaData = MetaDataFactory.CreateImageMetaData(key, extension, aspectRatio);

            // Assert
            Assert.That(imageMetaData, Is.TypeOf<ImageMetaData>(), "Should create ImageMetaData for Image type");
            Assert.That(imageMetaData.MediaKey, Is.EqualTo(key), "MediaKey should match");
            Assert.That(imageMetaData.Extension, Is.EqualTo(extension), "Extension should match");
            Assert.That(imageMetaData.AspectRatio, Is.EqualTo(aspectRatio), "Aspect ratio should be set");
        }

        [Test]
        public void MetaDataFactory_CreatesAudioMetaData()
        {
            // Arrange
            var key = "test-audio";
            var extension = ".mp3";
            var duration = 120.0;
            var bitrate = 320;

            // Act
            var audioMetaData = MetaDataFactory.CreateAudioMetaData(key, extension, duration, bitrate);

            // Assert
            Assert.That(audioMetaData, Is.TypeOf<AudioMetaData>(), "Should create AudioMetaData for Audio type");
            Assert.That(audioMetaData.MediaKey, Is.EqualTo(key), "MediaKey should match");
            Assert.That(audioMetaData.Extension, Is.EqualTo(extension), "Extension should match");
            Assert.That(audioMetaData.Duration, Is.EqualTo(duration), "Duration should be set");
            Assert.That(audioMetaData.Bitrate, Is.EqualTo(bitrate), "Bitrate should be set");
        }

        [Test]
        public void AudioMetaData_CanBeCreated()
        {
            // Arrange
            var key = "test-audio";
            var extension = ".mp3";
            var duration = 180.5;
            var bitrate = 256;

            // Act
            var audioMetaData = new AudioMetaData(key, extension, duration, bitrate);

            // Assert
            Assert.That(audioMetaData.MediaKey, Is.EqualTo(key), "MediaKey should match");
            Assert.That(audioMetaData.Extension, Is.EqualTo(extension), "Extension should match");
            Assert.That(audioMetaData.Duration, Is.EqualTo(duration), "Duration should match");
            Assert.That(audioMetaData.Bitrate, Is.EqualTo(bitrate), "Bitrate should match");
        }

        [Test]
        public void MetaDataFactory_CreateFromMedia_CreatesImageMetaData()
        {
            // Arrange
            var key = "test-image";
            var extension = ".png";
            var imageBinary = TestData.CreateTestImageBinary(1.77);

            // Act
            var metaData = MetaDataFactory.CreateFromMedia(MediaVaultType.Image, key, extension, imageBinary);

            // Assert
            Assert.That(metaData, Is.TypeOf<ImageMetaData>(), "Should create ImageMetaData from ImageBinary");
            var imageMetaData = (ImageMetaData)metaData;
            Assert.That(imageMetaData.AspectRatio, Is.EqualTo(1.77), "Should extract aspect ratio from ImageBinary");
        }

        [Test]
        public void MetaDataFactory_CreateFromMedia_CreatesAudioMetaData()
        {
            // Arrange
            var key = "test-audio";
            var extension = ".mp3";
            var audioBinary = TestData.CreateTestAudioBinary(240.5, 192);

            // Act
            var metaData = MetaDataFactory.CreateFromMedia(MediaVaultType.Audio, key, extension, audioBinary);

            // Assert
            Assert.That(metaData, Is.TypeOf<AudioMetaData>(), "Should create AudioMetaData from AudioBinary");
            var audioMetaData = (AudioMetaData)metaData;
            Assert.That(audioMetaData.Duration, Is.EqualTo(240.5), "Should extract duration from AudioBinary");
            Assert.That(audioMetaData.Bitrate, Is.EqualTo(192), "Should extract bitrate from AudioBinary");
        }
    }

    [TestFixture]
    public class MediaFactoryTests
    {
        [Test]
        public void MediaBinaryFactory_CreatesMediaBinary()
        {
            // Arrange
            var buffer = TestData.TestPngBytes;
            var size = buffer.Length;
            var extension = ".png";

            // Act
            var mediaParams = new MediaBinaryParams(buffer, size, extension);
            var mediaBinary = FileBinaryFactory.Create(MediaVaultType.Media, mediaParams);

            // Assert
            Assert.That(mediaBinary, Is.TypeOf<MediaBinary>(), "Should create MediaBinary for Media type");
            var typedMediaBinary = (MediaBinary)mediaBinary;
            Assert.That(typedMediaBinary.Buffer, Is.EqualTo(buffer), "Buffer should match");
            Assert.That(typedMediaBinary.Size, Is.EqualTo(size), "Size should match");
            Assert.That(typedMediaBinary.Extension, Is.EqualTo(extension), "Extension should match");
        }

        [Test]
        public void MediaBinaryFactory_CreatesImageBinary()
        {
            // Arrange
            var buffer = TestData.TestPngBytes;
            var size = buffer.Length;
            var extension = ".png";
            var aspectRatio = 1.77;

            // Act
            var imageParams = new ImageBinaryParams(buffer, size, extension, aspectRatio);
            var imageBinary = FileBinaryFactory.Create(MediaVaultType.Image, imageParams);

            // Assert
            Assert.That(imageBinary, Is.TypeOf<ImageBinary>(), "Should create ImageBinary for Image type");
            var typedImageBinary = (ImageBinary)imageBinary;
            Assert.That(typedImageBinary.Buffer, Is.EqualTo(buffer), "Buffer should match");
            Assert.That(typedImageBinary.Size, Is.EqualTo(size), "Size should match");
            Assert.That(typedImageBinary.Extension, Is.EqualTo(extension), "Extension should match");
            Assert.That(typedImageBinary.AspectRatio, Is.EqualTo(aspectRatio), "Aspect ratio should be set");
        }

        [Test]
        public void MediaBinaryFactory_CreatesAudioBinary()
        {
            // Arrange
            var buffer = TestData.TestPngBytes;
            var size = buffer.Length;
            var extension = ".mp3";
            var duration = 180.5;
            var bitrate = 256;

            // Act
            var audioParams = new AudioBinaryParams(buffer, size, extension, duration, bitrate);
            var audioBinary = FileBinaryFactory.Create(MediaVaultType.Audio, audioParams);

            // Assert
            Assert.That(audioBinary, Is.TypeOf<AudioBinary>(), "Should create AudioBinary for Audio type");
            var typedAudioBinary = (AudioBinary)audioBinary;
            Assert.That(typedAudioBinary.Buffer, Is.EqualTo(buffer), "Buffer should match");
            Assert.That(typedAudioBinary.Size, Is.EqualTo(size), "Size should match");
            Assert.That(typedAudioBinary.Extension, Is.EqualTo(extension), "Extension should match");
            Assert.That(typedAudioBinary.Duration, Is.EqualTo(duration), "Duration should be set");
            Assert.That(typedAudioBinary.Bitrate, Is.EqualTo(bitrate), "Bitrate should be set");
        }

        [Test]
        public void MediaBinaryFactory_ThrowsForInvalidType()
        {
            // Arrange
            var buffer = TestData.TestPngBytes;
            var size = buffer.Length;
            var extension = ".png";
            var invalidType = (MediaVaultType)999;

            // Act & Assert
            var invalidParams = new MediaBinaryParams(buffer, size, extension);
            
            Assert.Throws<ArgumentException>(() =>
            {
                FileBinaryFactory.Create(invalidType, invalidParams);
            }, "Should throw for invalid media vault type");
        }
    }
}
