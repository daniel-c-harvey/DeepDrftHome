using DeepDrftContent.Services.FileDatabase.Models;
using DeepDrftContent.Services.FileDatabase.Services;

namespace DeepDrftTests;

/// <summary>
/// Tests for FileDatabase functionality, ported from TypeScript tests
/// </summary>
[TestFixture]
public class FileDatabaseTests
{
    private string _testDatabasePath = null!;
    private FileDatabase? _fileDatabase;

    [SetUp]
    public void SetUp()
    {
        // Create a unique test directory for each test
        _testDatabasePath = Path.Combine(Path.GetTempPath(), "DeepDrftTests", Guid.NewGuid().ToString());
        
        // Clean up any existing test directory
        if (Directory.Exists(_testDatabasePath))
        {
            Directory.Delete(_testDatabasePath, true);
        }

        // Ensure the directory exists
        Directory.CreateDirectory(_testDatabasePath);
    }

    [TearDown]
    public void TearDown()
    {
        // Clean up test directory
        if (Directory.Exists(_testDatabasePath))
        {
            try
            {
                Directory.Delete(_testDatabasePath, true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    [Test]
    public async Task FileDatabase_CanBeCreatedAtSpecifiedLocation()
    {
        // Act
        _fileDatabase = await FileDatabase.FromAsync(_testDatabasePath);

        // Assert
        Assert.That(_fileDatabase, Is.Not.Null, "FileDatabase should not be null");
        Assert.That(_fileDatabase.GetIndexSize(), Is.EqualTo(0), "Index should be empty initially");
    }

    [Test]
    public async Task FileDatabase_CanAddNewVaultForImages()
    {
        // Arrange
        _fileDatabase = await FileDatabase.FromAsync(_testDatabasePath);
        Assert.That(_fileDatabase, Is.Not.Null);

        // Act
        await _fileDatabase.CreateVaultAsync(TestData.TestKeys.ImageVaultKey, TestData.TestKeys.ImageVaultType);

        // Assert
        Assert.That(_fileDatabase.GetIndexSize(), Is.EqualTo(1), "Index should contain one element");
        
        var vaultDirectory = Path.Combine(_testDatabasePath, TestData.TestKeys.ImageVaultKey);
        Assert.That(Directory.Exists(vaultDirectory), Is.True, "Vault directory should exist");
    }

    [Test]
    public async Task FileDatabase_CanAddNewMediaToImageVault()
    {
        // Arrange
        _fileDatabase = await FileDatabase.FromAsync(_testDatabasePath);
        Assert.That(_fileDatabase, Is.Not.Null);
        
        await _fileDatabase.CreateVaultAsync(TestData.TestKeys.ImageVaultKey, TestData.TestKeys.ImageVaultType);
        var testImage = TestData.CreateTestImageBinary(1.0);

        // Act
        await _fileDatabase.RegisterResourceAsync(
            TestData.TestKeys.ImageVaultKey,
            TestData.TestKeys.TestImageEntry,
            testImage);

        // Assert
        var vault = _fileDatabase.GetVault(TestData.TestKeys.ImageVaultKey);
        Assert.That(vault, Is.Not.Null, "Vault should not be null");
        Assert.That(vault!.HasIndexEntry(TestData.TestKeys.TestImageEntry), Is.True, 
            "Added image should be in the index");
    }

    [Test]
    public async Task FileDatabase_CanLoadValidResourceFromVault()
    {
        // Arrange
        _fileDatabase = await FileDatabase.FromAsync(_testDatabasePath);
        Assert.That(_fileDatabase, Is.Not.Null);
        
        await _fileDatabase.CreateVaultAsync(TestData.TestKeys.ImageVaultKey, TestData.TestKeys.ImageVaultType);
        var testImage = TestData.CreateTestImageBinary(1.0);
        
        await _fileDatabase.RegisterResourceAsync(
            TestData.TestKeys.ImageVaultKey,
            TestData.TestKeys.TestImageEntry,
            testImage);

        // Act
        var loadedMedia = await _fileDatabase.LoadResourceAsync<ImageBinary>(
            TestData.TestKeys.ImageVaultKey,
            TestData.TestKeys.TestImageEntry);

        // Assert
        Assert.That(loadedMedia, Is.Not.Null, "Loaded media should not be null");
        AssertValidImageResource(loadedMedia!);
    }

    [Test]
    public async Task FileDatabase_DeniesAccessToNonexistentVault()
    {
        // Arrange
        _fileDatabase = await FileDatabase.FromAsync(_testDatabasePath);
        Assert.That(_fileDatabase, Is.Not.Null);

        // Act & Assert - Should not throw exception but return null/default
        var vault = _fileDatabase.GetVault(TestData.TestKeys.NonExistentVaultKey);
        Assert.That(vault, Is.Null, "Nonexistent vault should return null");

        // Loading from nonexistent vault should not throw but handle gracefully
        Assert.DoesNotThrowAsync(async () =>
        {
            await _fileDatabase.LoadResourceAsync<ImageBinary>(
                TestData.TestKeys.NonExistentVaultKey,
                TestData.TestKeys.NonExistentEntryKey);
        }, "Should not throw exceptions when accessing nonexistent vault");
    }

    [Test]
    public async Task FileDatabase_DeniesAccessToNonexistentResource()
    {
        // Arrange
        _fileDatabase = await FileDatabase.FromAsync(_testDatabasePath);
        Assert.That(_fileDatabase, Is.Not.Null);
        
        await _fileDatabase.CreateVaultAsync(TestData.TestKeys.ImageVaultKey, TestData.TestKeys.ImageVaultType);

        // Act & Assert - Should not throw exception when accessing nonexistent resource
        Assert.DoesNotThrowAsync(async () =>
        {
            await _fileDatabase.LoadResourceAsync<ImageBinary>(
                TestData.TestKeys.ImageVaultKey,
                TestData.TestKeys.NonExistentEntryKey);
        }, "Should not throw exceptions when accessing nonexistent resource");
    }

    [Test]
    public async Task FileDatabase_CanBeReloadedFromSecondaryMemory()
    {
        // Arrange - Create and populate a database
        _fileDatabase = await FileDatabase.FromAsync(_testDatabasePath);
        Assert.That(_fileDatabase, Is.Not.Null);
        
        await _fileDatabase.CreateVaultAsync(TestData.TestKeys.ImageVaultKey, TestData.TestKeys.ImageVaultType);
        var testImage = TestData.CreateTestImageBinary(1.0);
        
        await _fileDatabase.RegisterResourceAsync(
            TestData.TestKeys.ImageVaultKey,
            TestData.TestKeys.TestImageEntry,
            testImage);

        // Act - Reload the database from the same path
        var reloadedDatabase = await FileDatabase.FromAsync(_testDatabasePath);

        // Assert
        Assert.That(reloadedDatabase, Is.Not.Null, "Reloaded database should not be null");
        Assert.That(reloadedDatabase.GetIndexSize(), Is.EqualTo(1), "Index count should be 1");
        
        // Verify vault exists
        Assert.That(reloadedDatabase.HasIndexEntry(TestData.TestKeys.ImageVaultKey), Is.True, 
            "Vault should be present in index");
        Assert.That(reloadedDatabase.HasVault(TestData.TestKeys.ImageVaultKey), Is.True, 
            "Vault should be present in vault collection");
        
        var vault = reloadedDatabase.GetVault(TestData.TestKeys.ImageVaultKey);
        Assert.That(vault, Is.Not.Null, "Vault should not be null");

        // Verify resource can be loaded
        var loadedMedia = await reloadedDatabase.LoadResourceAsync<ImageBinary>(
            TestData.TestKeys.ImageVaultKey,
            TestData.TestKeys.TestImageEntry);

        Assert.That(loadedMedia, Is.Not.Null, "Loaded media should not be null");
        AssertValidImageResource(loadedMedia!);
    }

    /// <summary>
    /// Helper method to validate an ImageBinary resource matches test expectations
    /// </summary>
    private static void AssertValidImageResource(ImageBinary media)
    {
        Assert.That(media, Is.Not.Null, "Image package should not be null");
        Assert.That(media.Size, Is.GreaterThan(0), "Image size should be greater than 0");
        Assert.That(media.Buffer.Length, Is.EqualTo(TestData.TestPngBytes.Length), 
            "Number of bytes should match test data");

        // Verify byte-by-byte equality
        for (int i = 0; i < media.Buffer.Length; i++)
        {
            Assert.That(media.Buffer[i], Is.EqualTo(TestData.TestPngBytes[i]), 
                $"Byte at index {i} should be equal");
        }

        Assert.That(media.Extension, Is.EqualTo(".png"), "Extension should be .png");
        Assert.That(media.AspectRatio, Is.EqualTo(1.0).Within(0.001), "Aspect ratio should be 1.0");
    }
}
