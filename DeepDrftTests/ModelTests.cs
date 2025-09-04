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
        public void MetaDataFactory_CreatesCorrectTypes()
        {
            // Arrange
            var key = "test";
            var extension = ".png";
            var aspectRatio = 2.0;

            // Act
            var mediaMetaData = MetaDataFactory.Create(MediaVaultType.Media, key, extension, 0.0);
            var imageMetaData = MetaDataFactory.Create(MediaVaultType.Image, key, extension, aspectRatio);

            // Assert
            Assert.That(mediaMetaData, Is.TypeOf<MetaData>(), "Should create MetaData for Media type");
            Assert.That(imageMetaData, Is.TypeOf<ImageMetaData>(), "Should create ImageMetaData for Image type");

            var typedImageMetaData = (ImageMetaData)imageMetaData;
            Assert.That(typedImageMetaData.AspectRatio, Is.EqualTo(aspectRatio), "Aspect ratio should be set");
        }
    }

    [TestFixture]
    public class MediaFactoryTests
    {
        [Test]
        public void MediaBinaryFactory_CreatesCorrectTypes()
        {
            // Arrange
            var buffer = TestData.TestPngBytes;
            var size = buffer.Length;
            var extension = ".png";

            // Act
            var mediaParams = new MediaBinaryParams(buffer, size, extension);
            var imageParams = new ImageBinaryParams(buffer, size, extension, 1.0);
            
            var mediaBinary = FileBinaryFactory.Create(MediaVaultType.Media, mediaParams);
            var imageBinary = FileBinaryFactory.Create(MediaVaultType.Image, imageParams);

            // Assert
            Assert.That(mediaBinary, Is.TypeOf<MediaBinary>(), "Should create MediaBinary for Media type");
            Assert.That(imageBinary, Is.TypeOf<ImageBinary>(), "Should create ImageBinary for Image type");

            var typedImageBinary = (ImageBinary)imageBinary;
            Assert.That(typedImageBinary.AspectRatio, Is.EqualTo(1.0), "Aspect ratio should be set");
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
