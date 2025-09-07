using DeepDrftContent.Services.FileDatabase.Abstractions;
using DeepDrftContent.Services.FileDatabase.Models;
using DeepDrftContent.Services.FileDatabase.Services;
using DeepDrftContent.Services.FileDatabase.Utils;

namespace DeepDrftTests;

/// <summary>
/// SOLID, DRY tests for IndexSystem components
/// Follows Single Responsibility: Each test class tests one concern
/// Follows DRY: Shared setup and helper methods
/// </summary>
[TestFixture]
public class IndexSystemTests
{
    /// <summary>
    /// Base test class for index-related tests - DRY principle
    /// </summary>
    public abstract class IndexTestBase
    {
        protected string TestDirectory { get; private set; } = null!;
        protected string IndexPath => Path.Combine(TestDirectory, "index");

        [SetUp]
        public virtual void SetUp()
        {
            TestDirectory = Path.Combine(Path.GetTempPath(), "DeepDrftTests", "IndexSystem", Guid.NewGuid().ToString());
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
        /// Helper method to create test entry IDs - DRY principle
        /// </summary>
        protected static string CreateTestEntryId(string key)
            => key;

        /// <summary>
        /// Helper method to create test metadata - DRY principle
        /// </summary>
        protected static MetaData CreateTestMetaData(string key, string extension = ".png")
            => new(key, extension);
    }

    /// <summary>
    /// Tests for IndexFactoryService - Single Responsibility Principle
    /// </summary>
    [TestFixture]
    public class IndexFactoryServiceTests : IndexTestBase
    {
        private IndexFactoryService _factory = null!;

        [SetUp]
        public override void SetUp()
        {
            base.SetUp();
            _factory = new IndexFactoryService();
        }

        [Test]
        public async Task CreateIndexAsync_DirectoryType_CreatesDirectoryIndex()
        {
            // Act
            var index = await _factory.CreateDirectoryIndexAsync(TestDirectory);

            // Assert
            Assert.That(index, Is.Not.Null, "Index should be created");
            Assert.That(index, Is.TypeOf<DirectoryIndex>(), "Should create DirectoryIndex");
            Assert.That(File.Exists(IndexPath), Is.True, "Index file should be created");
        }

        [Test]
        public async Task CreateIndexAsync_VaultType_CreatesVaultIndex()
        {
            // Act
            var index = await _factory.CreateVaultIndexAsync(TestDirectory, MediaVaultType.Media);

            // Assert
            Assert.That(index, Is.Not.Null, "Index should be created");
            Assert.That(index, Is.TypeOf<VaultIndex>(), "Should create VaultIndex");
            Assert.That(File.Exists(IndexPath), Is.True, "Index file should be created");
        }


        [Test]
        public async Task LoadIndexAsync_ExistingDirectoryIndex_LoadsSuccessfully()
        {
            // Arrange - Create an index first
            await _factory.CreateDirectoryIndexAsync(TestDirectory);

            // Act
            var loadedIndex = await _factory.LoadIndexAsync(IndexType.Directory, TestDirectory);

            // Assert
            Assert.That(loadedIndex, Is.Not.Null, "Index should be loaded");
            Assert.That(loadedIndex, Is.TypeOf<DirectoryIndex>(), "Should load DirectoryIndex");
        }

        [Test]
        public async Task LoadIndexAsync_ExistingVaultIndex_LoadsSuccessfully()
        {
            // Arrange - Create an index first
            await _factory.CreateVaultIndexAsync(TestDirectory, MediaVaultType.Media);

            // Act
            var loadedIndex = await _factory.LoadIndexAsync(IndexType.Vault, TestDirectory);

            // Assert
            Assert.That(loadedIndex, Is.Not.Null, "Index should be loaded");
            Assert.That(loadedIndex, Is.TypeOf<VaultIndex>(), "Should load VaultIndex");
        }

        [Test]
        public void LoadIndexAsync_NonExistentIndex_ThrowsException()
        {
            // Act & Assert
            Assert.ThrowsAsync<FileNotFoundException>(async () =>
                await _factory.LoadIndexAsync(IndexType.Directory, TestDirectory),
                "Should throw when index file doesn't exist");
        }

        [Test]
        public async Task LoadOrCreateIndexAsync_ExistingIndex_LoadsExisting()
        {
            // Arrange - Create an index with data
            var originalIndex = await _factory.CreateDirectoryIndexAsync(TestDirectory);
            Assert.That(originalIndex, Is.TypeOf<DirectoryIndex>(), "Should create DirectoryIndex");
            
            var directoryIndex = (DirectoryIndex)originalIndex!;
            var testKey = CreateTestEntryId("test-entry");
            directoryIndex.PutEntry(testKey);

            // Save the modified index
            var indexData = _factory.CreateIndexData(IndexType.Directory, directoryIndex);
            await FileUtils.PutObjectAsync(IndexPath, indexData);

            // Act
            var loadedIndex = await _factory.LoadOrCreateDirectoryIndexAsync(TestDirectory);

            // Assert
            Assert.That(loadedIndex, Is.Not.Null, "Index should be loaded");
            Assert.That(loadedIndex, Is.TypeOf<DirectoryIndex>(), "Should load DirectoryIndex");
            Assert.That(loadedIndex, Is.InstanceOf<IEntryQueryable>(), "Should implement IEntryQueryable");
            
            Assert.That(loadedIndex.GetEntriesSize(), Is.EqualTo(1), "Should preserve existing entries");
        }

        [Test]
        public async Task LoadOrCreateIndexAsync_NonExistentIndex_CreatesNew()
        {
            // Act
            var index = await _factory.LoadOrCreateDirectoryIndexAsync(TestDirectory);

            // Assert
            Assert.That(index, Is.Not.Null, "Index should be created");
            Assert.That(index, Is.TypeOf<DirectoryIndex>(), "Should create DirectoryIndex");
            Assert.That(index, Is.InstanceOf<IEntryQueryable>(), "Should implement IEntryQueryable");
            
            Assert.That(index.GetEntriesSize(), Is.EqualTo(0), "New index should be empty");
        }

        [Test]
        public void CreateIndexData_DirectoryIndex_CreatesDirectoryIndexData()
        {
            // Arrange
            var directoryIndex = new DirectoryIndex(new DirectoryIndexData("test"));
            var testKey = CreateTestEntryId("test-entry");
            directoryIndex.PutEntry(testKey);

            // Act
            var indexData = _factory.CreateIndexData(IndexType.Directory, directoryIndex);

            // Assert
            Assert.That(indexData, Is.TypeOf<DirectoryIndexData>(), "Should create DirectoryIndexData");
            var typedData = (DirectoryIndexData)indexData;
            Assert.That(typedData.IndexKey, Is.EqualTo("test"), "Should preserve index key");
        }

        [Test]
        public void CreateIndexData_VaultIndex_CreatesVaultIndexData()
        {
            // Arrange
            var vaultIndex = new VaultIndex(new VaultIndexData("test"));
            var testKey = CreateTestEntryId("test-entry");
            var testMetaData = CreateTestMetaData("test-entry");
            vaultIndex.PutEntry(testKey, testMetaData);

            // Act
            var indexData = _factory.CreateIndexData(IndexType.Vault, vaultIndex);

            // Assert
            Assert.That(indexData, Is.TypeOf<VaultIndexData>(), "Should create VaultIndexData");
            var typedData = (VaultIndexData)indexData;
            Assert.That(typedData.IndexKey, Is.EqualTo("test"), "Should preserve index key");
        }

        [Test]
        public void CreateIndexFromData_DirectoryIndexData_CreatesDirectoryIndex()
        {
            // Arrange
            var indexData = new DirectoryIndexData("test");

            // Act
            var index = _factory.CreateIndexFromData(IndexType.Directory, indexData);

            // Assert
            Assert.That(index, Is.TypeOf<DirectoryIndex>(), "Should create DirectoryIndex");
            var queryableIndex = (IEntryQueryable)index;
            Assert.That(queryableIndex.GetEntriesSize(), Is.EqualTo(0), "Should be empty initially");
        }

        [Test]
        public void CreateIndexFromData_VaultIndexData_CreatesVaultIndex()
        {
            // Arrange
            var indexData = new VaultIndexData("test");

            // Act
            var index = _factory.CreateIndexFromData(IndexType.Vault, indexData);

            // Assert
            Assert.That(index, Is.TypeOf<VaultIndex>(), "Should create VaultIndex");
            var queryableIndex = (IEntryQueryable)index;
            Assert.That(queryableIndex.GetEntriesSize(), Is.EqualTo(0), "Should be empty initially");
        }
    }

    /// <summary>
    /// Tests for DirectoryIndex - Single Responsibility Principle
    /// </summary>
    [TestFixture]
    public class DirectoryIndexTests : IndexTestBase
    {
        private DirectoryIndex _directoryIndex = null!;

        [SetUp]
        public override void SetUp()
        {
            base.SetUp();
            _directoryIndex = new DirectoryIndex(new DirectoryIndexData("test-directory"));
        }

        [Test]
        public void DirectoryIndex_InitialState_IsEmpty()
        {
            // Assert
            Assert.That(_directoryIndex.GetEntriesSize(), Is.EqualTo(0), "Should be empty initially");
            Assert.That(_directoryIndex.GetEntries(), Is.Empty, "Should have no entries");
        }

        [Test]
        public void PutEntry_NewEntry_AddsSuccessfully()
        {
            // Arrange
            var testKey = CreateTestEntryId("new-entry");

            // Act
            _directoryIndex.PutEntry(testKey);

            // Assert
            Assert.That(_directoryIndex.GetEntriesSize(), Is.EqualTo(1), "Should have one entry");
            Assert.That(_directoryIndex.HasEntry(testKey), Is.True, "Should contain the entry");
            Assert.That(_directoryIndex.GetEntries(), Contains.Item(testKey), "Should include entry in collection");
        }

        [Test]
        public void PutEntry_DuplicateEntry_DoesNotDuplicate()
        {
            // Arrange
            var testKey = CreateTestEntryId("duplicate-entry");
            _directoryIndex.PutEntry(testKey);

            // Act
            _directoryIndex.PutEntry(testKey);

            // Assert
            Assert.That(_directoryIndex.GetEntriesSize(), Is.EqualTo(1), "Should still have only one entry");
        }

        [Test]
        public void PutEntry_MultipleEntries_AddsAll()
        {
            // Arrange
            var keys = new[]
            {
                CreateTestEntryId("entry1"),
                CreateTestEntryId("entry2"),
                CreateTestEntryId("entry3")
            };

            // Act
            foreach (var key in keys)
            {
                _directoryIndex.PutEntry(key);
            }

            // Assert
            Assert.That(_directoryIndex.GetEntriesSize(), Is.EqualTo(3), "Should have three entries");
            foreach (var key in keys)
            {
                Assert.That(_directoryIndex.HasEntry(key), Is.True, $"Should contain {key}");
            }
        }

        [Test]
        public void HasEntry_ExistingEntry_ReturnsTrue()
        {
            // Arrange
            var testKey = CreateTestEntryId("existing-entry");
            _directoryIndex.PutEntry(testKey);

            // Act & Assert
            Assert.That(_directoryIndex.HasEntry(testKey), Is.True, "Should find existing entry");
        }

        [Test]
        public void HasEntry_NonExistentEntry_ReturnsFalse()
        {
            // Arrange
            var testKey = CreateTestEntryId("non-existent");

            // Act & Assert
            Assert.That(_directoryIndex.HasEntry(testKey), Is.False, "Should not find non-existent entry");
        }

        [Test]
        public void GetEntries_WithMultipleEntries_ReturnsAllEntries()
        {
            // Arrange
            var keys = new[]
            {
                CreateTestEntryId("entry1"),
                CreateTestEntryId("entry2"),
                CreateTestEntryId("entry3")
            };

            foreach (var key in keys)
            {
                _directoryIndex.PutEntry(key);
            }

            // Act
            var entries = _directoryIndex.GetEntries();

            // Assert
            Assert.That(entries.Count, Is.EqualTo(3), "Should return all entries");
            foreach (var key in keys)
            {
                Assert.That(entries, Contains.Item(key), $"Should contain {key}");
            }
        }
    }

    /// <summary>
    /// Tests for VaultIndex - Single Responsibility Principle
    /// </summary>
    [TestFixture]
    public class VaultIndexTests : IndexTestBase
    {
        private VaultIndex _vaultIndex = null!;

        [SetUp]
        public override void SetUp()
        {
            base.SetUp();
            _vaultIndex = new VaultIndex(new VaultIndexData("test-vault"));
        }

        [Test]
        public void VaultIndex_InitialState_IsEmpty()
        {
            // Assert
            Assert.That(_vaultIndex.GetEntriesSize(), Is.EqualTo(0), "Should be empty initially");
            Assert.That(_vaultIndex.GetEntries(), Is.Empty, "Should have no entries");
        }

        [Test]
        public void PutEntry_NewEntryWithMetadata_AddsSuccessfully()
        {
            // Arrange
            var testKey = CreateTestEntryId("new-entry");
            var testMetaData = CreateTestMetaData("new-entry");

            // Act
            _vaultIndex.PutEntry(testKey, testMetaData);

            // Assert
            Assert.That(_vaultIndex.GetEntriesSize(), Is.EqualTo(1), "Should have one entry");
            Assert.That(_vaultIndex.HasEntry(testKey), Is.True, "Should contain the entry");
            Assert.That(_vaultIndex.GetEntry(testKey), Is.EqualTo(testMetaData), "Should return correct metadata");
        }

        [Test]
        public void PutEntry_DuplicateEntry_UpdatesMetadata()
        {
            // Arrange
            var testKey = CreateTestEntryId("duplicate-entry");
            var originalMetaData = CreateTestMetaData("original");
            var updatedMetaData = CreateTestMetaData("updated");

            _vaultIndex.PutEntry(testKey, originalMetaData);

            // Act
            _vaultIndex.PutEntry(testKey, updatedMetaData);

            // Assert
            Assert.That(_vaultIndex.GetEntriesSize(), Is.EqualTo(1), "Should still have only one entry");
            Assert.That(_vaultIndex.GetEntry(testKey), Is.EqualTo(updatedMetaData), "Should have updated metadata");
        }

        [Test]
        public void GetEntry_ExistingEntry_ReturnsMetadata()
        {
            // Arrange
            var testKey = CreateTestEntryId("existing-entry");
            var testMetaData = CreateTestMetaData("existing-entry");
            _vaultIndex.PutEntry(testKey, testMetaData);

            // Act
            var retrievedMetaData = _vaultIndex.GetEntry(testKey);

            // Assert
            Assert.That(retrievedMetaData, Is.EqualTo(testMetaData), "Should return correct metadata");
        }

        [Test]
        public void GetEntry_NonExistentEntry_ReturnsNull()
        {
            // Arrange
            var testKey = CreateTestEntryId("non-existent");

            // Act
            var retrievedMetaData = _vaultIndex.GetEntry(testKey);

            // Assert
            Assert.That(retrievedMetaData, Is.Null, "Should return null for non-existent entry");
        }

        [Test]
        public void PutEntry_MultipleEntriesWithDifferentMetadata_AddsAll()
        {
            // Arrange
            var entries = new[]
            {
                (CreateTestEntryId("entry1"), CreateTestMetaData("entry1", ".png")),
                (CreateTestEntryId("entry2"), CreateTestMetaData("entry2", ".jpg")),
                (CreateTestEntryId("entry3"), CreateTestMetaData("entry3", ".gif"))
            };

            // Act
            foreach (var (key, metaData) in entries)
            {
                _vaultIndex.PutEntry(key, metaData);
            }

            // Assert
            Assert.That(_vaultIndex.GetEntriesSize(), Is.EqualTo(3), "Should have three entries");
            foreach (var (key, metaData) in entries)
            {
                Assert.That(_vaultIndex.HasEntry(key), Is.True, $"Should contain {key}");
                Assert.That(_vaultIndex.GetEntry(key), Is.EqualTo(metaData), $"Should have correct metadata for {key}");
            }
        }
    }


    /// <summary>
    /// Integration tests for IndexDirectory classes - Open/Closed Principle
    /// </summary>
    [TestFixture]
    public class IndexDirectoryIntegrationTests : IndexTestBase
    {
        [Test]
        public async Task DirectoryIndexDirectory_AddToIndex_PersistsChanges()
        {
            // This test would require access to protected methods, so we'll test through the FileDatabase instead
            // which properly encapsulates the DirectoryIndexDirectory functionality
            
            // Arrange
            var database = await FileDatabase.FromAsync(TestDirectory);
            var testVaultKey = CreateTestEntryId("test-vault");

            // Act - This internally uses DirectoryIndexDirectory.AddToIndexAsync
            await database!.CreateVaultAsync(testVaultKey, MediaVaultType.Image);

            // Assert
            Assert.That(database.HasIndexEntry(testVaultKey), Is.True, "Should contain added entry");
            Assert.That(database.GetIndexSize(), Is.EqualTo(1), "Should have one entry");
            Assert.That(File.Exists(IndexPath), Is.True, "Should persist to file");

            // Verify persistence by reloading database
            var reloadedDatabase = await FileDatabase.FromAsync(TestDirectory);
            Assert.That(reloadedDatabase!.HasIndexEntry(testVaultKey), Is.True, "Should persist entry across restarts");
        }

        [Test]
        public async Task VaultIndexDirectory_AddToIndex_PersistsChanges()
        {
            // This test would require access to protected methods, so we'll test through MediaVault instead
            // which properly encapsulates the VaultIndexDirectory functionality
            
            // Arrange
            var vault = await ImageVault.FromAsync(TestDirectory);
            var testKey = CreateTestEntryId("test-entry");
            var testImage = TestData.CreateTestImageBinary(1.0);

            // Act - This internally uses VaultIndexDirectory.AddToIndexAsync
            await vault!.AddEntryAsync(testKey, testImage);

            // Assert
            Assert.That(vault.HasIndexEntry(testKey), Is.True, "Should contain added entry");
            Assert.That(vault.GetIndexSize(), Is.EqualTo(1), "Should have one entry");
            Assert.That(File.Exists(IndexPath), Is.True, "Should persist to file");

            // Verify persistence by reloading vault
            var reloadedVault = await ImageVault.FromAsync(TestDirectory);
            Assert.That(reloadedVault!.HasIndexEntry(testKey), Is.True, "Should persist entry across restarts");
        }
    }
}
