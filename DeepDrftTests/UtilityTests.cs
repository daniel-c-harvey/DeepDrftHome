using DeepDrftContent.FileDatabase.Models;
using DeepDrftContent.FileDatabase.Utils;

namespace DeepDrftTests;

/// <summary>
/// Tests for utility classes like StructuralMap and StructuralSet
/// </summary>
[TestFixture]
public class UtilityTests
{
    [TestFixture]
    public class StructuralMapTests
    {
        [Test]
        public void StructuralMap_CanAddAndRetrieveEntries()
        {
            // Arrange
            var map = new StructuralMap<EntryKey, string>();
            var key = new EntryKey("test", MediaVaultType.Image);
            var value = "test-value";

            // Act
            map.Set(key, value);

            // Assert
            Assert.That(map.Has(key), Is.True, "Map should contain the key");
            Assert.That(map.Get(key), Is.EqualTo(value), "Retrieved value should match");
            Assert.That(map.Size, Is.EqualTo(1), "Map should have one entry");
        }

        [Test]
        public void StructuralMap_HandlesStructuralEquality()
        {
            // Arrange
            var map = new StructuralMap<EntryKey, string>();
            var key1 = new EntryKey("test", MediaVaultType.Image);
            var key2 = new EntryKey("test", MediaVaultType.Image); // Same values, different instance
            var value = "test-value";

            // Act
            map.Set(key1, value);

            // Assert
            Assert.That(map.Has(key2), Is.True, "Map should use structural equality");
            Assert.That(map.Get(key2), Is.EqualTo(value), "Should retrieve value using structurally equal key");
        }

        [Test]
        public void StructuralMap_CanRemoveEntries()
        {
            // Arrange
            var map = new StructuralMap<EntryKey, string>();
            var key = new EntryKey("test", MediaVaultType.Image);
            var value = "test-value";
            map.Set(key, value);

            // Act
            var removed = map.Delete(key);

            // Assert
            Assert.That(removed, Is.True, "Delete should return true");
            Assert.That(map.Has(key), Is.False, "Map should not contain the key after removal");
            Assert.That(map.Size, Is.EqualTo(0), "Map should be empty");
        }

        [Test]
        public void StructuralMap_CanEnumerateEntries()
        {
            // Arrange
            var map = new StructuralMap<EntryKey, string>();
            var entries = new[]
            {
                (new EntryKey("key1", MediaVaultType.Image), "value1"),
                (new EntryKey("key2", MediaVaultType.Media), "value2"),
                (new EntryKey("key3", MediaVaultType.Image), "value3")
            };

            foreach (var (key, value) in entries)
            {
                map.Set(key, value);
            }

            // Act
            var retrievedEntries = map.ToList();

            // Assert
            Assert.That(retrievedEntries.Count, Is.EqualTo(3), "Should enumerate all entries");
            
            foreach (var (key, value) in entries)
            {
                Assert.That(retrievedEntries.Any(kvp => kvp.Key.Equals(key) && kvp.Value == value), 
                    Is.True, $"Should contain entry ({key}, {value})");
            }
        }
    }

    [TestFixture]
    public class StructuralSetTests
    {
        [Test]
        public void StructuralSet_CanAddAndContainEntries()
        {
            // Arrange
            var set = new StructuralSet<EntryKey>();
            var key = new EntryKey("test", MediaVaultType.Image);

            // Act
            set.Add(key);

            // Assert
            Assert.That(set.Has(key), Is.True, "Set should contain the key");
            Assert.That(set.Size, Is.EqualTo(1), "Set should have one entry");
        }

        [Test]
        public void StructuralSet_HandlesStructuralEquality()
        {
            // Arrange
            var set = new StructuralSet<EntryKey>();
            var key1 = new EntryKey("test", MediaVaultType.Image);
            var key2 = new EntryKey("test", MediaVaultType.Image); // Same values, different instance

            // Act
            set.Add(key1);
            set.Add(key2);

            // Assert
            Assert.That(set.Has(key2), Is.True, "Should contain structurally equal key");
            Assert.That(set.Size, Is.EqualTo(1), "Set should still have only one entry due to structural equality");
        }

        [Test]
        public void StructuralSet_CanRemoveEntries()
        {
            // Arrange
            var set = new StructuralSet<EntryKey>();
            var key = new EntryKey("test", MediaVaultType.Image);
            set.Add(key);

            // Act
            var removed = set.Delete(key);

            // Assert
            Assert.That(removed, Is.True, "Delete should return true");
            Assert.That(set.Has(key), Is.False, "Set should not contain the key after removal");
            Assert.That(set.Size, Is.EqualTo(0), "Set should be empty");
        }

        [Test]
        public void StructuralSet_CanEnumerateEntries()
        {
            // Arrange
            var set = new StructuralSet<EntryKey>();
            var keys = new[]
            {
                new EntryKey("key1", MediaVaultType.Image),
                new EntryKey("key2", MediaVaultType.Media),
                new EntryKey("key3", MediaVaultType.Image)
            };

            foreach (var key in keys)
            {
                set.Add(key);
            }

            // Act
            var retrievedKeys = set.ToList();

            // Assert
            Assert.That(retrievedKeys.Count, Is.EqualTo(3), "Should enumerate all entries");
            
            foreach (var key in keys)
            {
                Assert.That(retrievedKeys.Contains(key), Is.True, $"Should contain key {key}");
            }
        }
    }

    [TestFixture]
    public class FileUtilsTests
    {
        private string _testDirectory = null!;

        [SetUp]
        public void SetUp()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), "DeepDrftTests", "FileUtils", Guid.NewGuid().ToString());
            Directory.CreateDirectory(_testDirectory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, true);
            }
        }

        [Test]
        public async Task FileUtils_CanWriteAndReadFile()
        {
            // Arrange
            var testFile = Path.Combine(_testDirectory, "test.dat");
            var testData = TestData.TestPngBytes;

            // Act
            await FileUtils.PutFileAsync(testFile, testData);
            var fileBinary = await FileUtils.FetchFileAsync(testFile);

            // Assert
            Assert.That(File.Exists(testFile), Is.True, "File should exist after writing");
            Assert.That(fileBinary, Is.Not.Null, "FileBinary should not be null");
            Assert.That(fileBinary.Buffer.Length, Is.EqualTo(testData.Length), "Read data length should match");
            
            for (int i = 0; i < testData.Length; i++)
            {
                Assert.That(fileBinary.Buffer[i], Is.EqualTo(testData[i]), $"Byte at index {i} should match");
            }
        }

        [Test]
        public async Task FileUtils_CanWriteAndReadJson()
        {
            // Arrange
            var testFile = Path.Combine(_testDirectory, "test.json");
            var testObject = new { Name = "Test", Value = 42, IsActive = true };

            // Act
            await FileUtils.PutObjectAsync(testFile, testObject);
            var readObject = await FileUtils.FetchObjectAsync<dynamic>(testFile);

            // Assert
            Assert.That(File.Exists(testFile), Is.True, "JSON file should exist after writing");
            Assert.That(readObject, Is.Not.Null, "Read object should not be null");
        }

        [Test]
        public void FileUtils_HandlesNonExistentFile()
        {
            // Arrange
            var nonExistentFile = Path.Combine(_testDirectory, "does-not-exist.dat");

            // Act & Assert
            Assert.ThrowsAsync<FileNotFoundException>(async () =>
            {
                await FileUtils.FetchFileAsync(nonExistentFile);
            }, "Should throw FileNotFoundException for non-existent file");
        }
    }
}
