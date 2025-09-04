using DeepDrftContent.FileDatabase.Abstractions;
using DeepDrftContent.FileDatabase.Models;
using DeepDrftContent.FileDatabase.Services;
using DeepDrftContent.FileDatabase.Utils;

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
        /// Helper method to create test entry keys - DRY principle
        /// </summary>
        protected static EntryKey CreateTestEntryKey(string key, MediaVaultType type = MediaVaultType.Image)
            => new(key, type);

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
            var index = await _factory.CreateIndexAsync(IndexType.Directory, TestDirectory);

            // Assert
            Assert.That(index, Is.Not.Null, "Index should be created");
            Assert.That(index, Is.TypeOf<DirectoryIndex>(), "Should create DirectoryIndex");
            Assert.That(File.Exists(IndexPath), Is.True, "Index file should be created");
        }

        [Test]
        public async Task CreateIndexAsync_VaultType_CreatesVaultIndex()
        {
            // Act
            var index = await _factory.CreateIndexAsync(IndexType.Vault, TestDirectory);

            // Assert
            Assert.That(index, Is.Not.Null, "Index should be created");
            Assert.That(index, Is.TypeOf<VaultIndex>(), "Should create VaultIndex");
            Assert.That(File.Exists(IndexPath), Is.True, "Index file should be created");
        }

        [Test]
        public void CreateIndexAsync_InvalidType_ThrowsArgumentException()
        {
            // Arrange
            var invalidType = (IndexType)999;

            // Act & Assert
            Assert.ThrowsAsync<ArgumentException>(async () =>
                await _factory.CreateIndexAsync(invalidType, TestDirectory),
                "Should throw for invalid index type");
        }

        [Test]
        public async Task LoadIndexAsync_ExistingDirectoryIndex_LoadsSuccessfully()
        {
            // Arrange - Create an index first
            await _factory.CreateIndexAsync(IndexType.Directory, TestDirectory);

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
            await _factory.CreateIndexAsync(IndexType.Vault, TestDirectory);

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
            var originalIndex = await _factory.CreateIndexAsync(IndexType.Directory, TestDirectory);
            Assert.That(originalIndex, Is.TypeOf<DirectoryIndex>(), "Should create DirectoryIndex");
            
            var directoryIndex = (DirectoryIndex)originalIndex!;
            var testKey = CreateTestEntryKey("test-entry");
            directoryIndex.PutEntry(testKey);

            // Save the modified index
            var indexData = _factory.CreateIndexData(IndexType.Directory, directoryIndex);
            await FileUtils.PutObjectAsync(IndexPath, indexData);

            // Act
            var loadedIndex = await _factory.LoadOrCreateIndexAsync(IndexType.Directory, TestDirectory);

            // Assert
            Assert.That(loadedIndex, Is.Not.Null, "Index should be loaded");
            Assert.That(loadedIndex, Is.TypeOf<DirectoryIndex>(), "Should load DirectoryIndex");
            Assert.That(loadedIndex, Is.InstanceOf<IEntryQueryable>(), "Should implement IEntryQueryable");
            
            var queryableIndex = (IEntryQueryable)loadedIndex!;
            Assert.That(queryableIndex.GetEntriesSize(), Is.EqualTo(1), "Should preserve existing entries");
        }

        [Test]
        public async Task LoadOrCreateIndexAsync_NonExistentIndex_CreatesNew()
        {
            // Act
            var index = await _factory.LoadOrCreateIndexAsync(IndexType.Directory, TestDirectory);

            // Assert
            Assert.That(index, Is.Not.Null, "Index should be created");
            Assert.That(index, Is.TypeOf<DirectoryIndex>(), "Should create DirectoryIndex");
            Assert.That(index, Is.InstanceOf<IEntryQueryable>(), "Should implement IEntryQueryable");
            
            var queryableIndex = (IEntryQueryable)index!;
            Assert.That(queryableIndex.GetEntriesSize(), Is.EqualTo(0), "New index should be empty");
        }

        [Test]
        public void CreateIndexData_DirectoryIndex_CreatesDirectoryIndexData()
        {
            // Arrange
            var directoryIndex = new DirectoryIndex(new DirectoryIndexData("test"));
            var testKey = CreateTestEntryKey("test-entry");
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
            var testKey = CreateTestEntryKey("test-entry");
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
            var testKey = CreateTestEntryKey("new-entry");

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
            var testKey = CreateTestEntryKey("duplicate-entry");
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
                CreateTestEntryKey("entry1"),
                CreateTestEntryKey("entry2"),
                CreateTestEntryKey("entry3")
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
            var testKey = CreateTestEntryKey("existing-entry");
            _directoryIndex.PutEntry(testKey);

            // Act & Assert
            Assert.That(_directoryIndex.HasEntry(testKey), Is.True, "Should find existing entry");
        }

        [Test]
        public void HasEntry_NonExistentEntry_ReturnsFalse()
        {
            // Arrange
            var testKey = CreateTestEntryKey("non-existent");

            // Act & Assert
            Assert.That(_directoryIndex.HasEntry(testKey), Is.False, "Should not find non-existent entry");
        }

        [Test]
        public void GetEntries_WithMultipleEntries_ReturnsAllEntries()
        {
            // Arrange
            var keys = new[]
            {
                CreateTestEntryKey("entry1"),
                CreateTestEntryKey("entry2"),
                CreateTestEntryKey("entry3")
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
            var testKey = CreateTestEntryKey("new-entry");
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
            var testKey = CreateTestEntryKey("duplicate-entry");
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
            var testKey = CreateTestEntryKey("existing-entry");
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
            var testKey = CreateTestEntryKey("non-existent");

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
                (CreateTestEntryKey("entry1"), CreateTestMetaData("entry1", ".png")),
                (CreateTestEntryKey("entry2"), CreateTestMetaData("entry2", ".jpg")),
                (CreateTestEntryKey("entry3"), CreateTestMetaData("entry3", ".gif"))
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
    /// Tests for IndexFactory - Single Responsibility Principle
    /// </summary>
    [TestFixture]
    public class IndexFactoryTests : IndexTestBase
    {
        [Test]
        public async Task IndexFactory_DirectoryType_BuildsDirectoryIndex()
        {
            // Arrange
            var factory = new IndexFactory(TestDirectory, IndexType.Directory);

            // Act
            var index = await factory.BuildIndexAsync();

            // Assert
            Assert.That(index, Is.Not.Null, "Index should be built");
            Assert.That(index, Is.TypeOf<DirectoryIndex>(), "Should build DirectoryIndex");
            Assert.That(File.Exists(IndexPath), Is.True, "Index file should be created");
        }

        [Test]
        public async Task IndexFactory_VaultType_BuildsVaultIndex()
        {
            // Arrange
            var factory = new IndexFactory(TestDirectory, IndexType.Vault);

            // Act
            var index = await factory.BuildIndexAsync();

            // Assert
            Assert.That(index, Is.Not.Null, "Index should be built");
            Assert.That(index, Is.TypeOf<VaultIndex>(), "Should build VaultIndex");
            Assert.That(File.Exists(IndexPath), Is.True, "Index file should be created");
        }

        [Test]
        public async Task IndexFactory_ExistingIndex_LoadsExistingData()
        {
            // Arrange - Create index with data
            var factory = new IndexFactory(TestDirectory, IndexType.Directory);
            var originalIndex = await factory.BuildIndexAsync();
            var directoryIndex = (DirectoryIndex)originalIndex!;
            var testKey = CreateTestEntryKey("persisted-entry");
            directoryIndex.PutEntry(testKey);

            // Save the index manually
            var factoryService = new IndexFactoryService();
            var indexData = factoryService.CreateIndexData(IndexType.Directory, directoryIndex);
            await FileUtils.PutObjectAsync(IndexPath, indexData);

            // Act - Create new factory and build
            var newFactory = new IndexFactory(TestDirectory, IndexType.Directory);
            var loadedIndex = await newFactory.BuildIndexAsync();

            // Assert
            Assert.That(loadedIndex, Is.Not.Null, "Index should be loaded");
            Assert.That(loadedIndex, Is.InstanceOf<IEntryQueryable>(), "Should implement IEntryQueryable");
            
            var queryableIndex = (IEntryQueryable)loadedIndex!;
            Assert.That(queryableIndex.GetEntriesSize(), Is.EqualTo(1), "Should load existing entry");
            Assert.That(queryableIndex.HasEntry(testKey), Is.True, "Should contain persisted entry");
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
            var testVaultKey = CreateTestEntryKey("test-vault");

            // Act - This internally uses DirectoryIndexDirectory.AddToIndexAsync
            await database!.CreateVaultAsync(testVaultKey);

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
            var testKey = CreateTestEntryKey("test-entry");
            var testImage = TestData.CreateTestImageBinary(1.0);

            // Act - This internally uses VaultIndexDirectory.AddToIndexAsync
            await vault!.AddEntryAsync(MediaVaultType.Image, testKey, testImage);

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
