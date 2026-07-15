using System.Text.Json;
using System.Text.Json.Nodes;
using Cursivis.Infrastructure.Storage.Persistence;
using Cursivis.Infrastructure.Storage.Settings;
using Cursivis.IntegrationTests.TestSupport;

namespace Cursivis.IntegrationTests.Storage;

public sealed class SettingsPersistenceTests
{
    [Fact]
    public async Task SaveAndLoad_RoundTripsVersionedDocumentAndRetainsBoundedBackup()
    {
        using var temporary = new TemporaryDirectory();
        var path = System.IO.Path.Combine(temporary.Path, "settings.json");
        var store = CreateStore(path, currentVersion: 2);

        await store.SaveAsync(new TestSettings("Guided", new TestHotkeys("Ctrl+Alt+O")));
        await store.SaveAsync(new TestSettings("Smart", new TestHotkeys("Ctrl+Alt+I")));

        var loaded = await store.LoadAsync();

        Assert.Equal(SettingsLoadStatus.Loaded, loaded.Status);
        Assert.Equal("Smart", loaded.Value.InteractionMode);
        Assert.Equal("Ctrl+Alt+I", loaded.Value.Hotkeys.ContextTrigger);
        Assert.True(loaded.RepairPersisted);
        Assert.Single(Directory.EnumerateFiles(temporary.Path, "settings.json.backup.*"));
        Assert.Empty(Directory.EnumerateFiles(temporary.Path, "*.tmp"));

        var root = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        Assert.Equal(2, root["schemaVersion"]!.GetValue<int>());
    }

    [Fact]
    public async Task Save_WhenInterruptedBeforeCommit_PreservesPreviousFileAndCleansTemporaryFile()
    {
        using var temporary = new TemporaryDirectory();
        var path = System.IO.Path.Combine(temporary.Path, "settings.json");
        var originalStore = CreateStore(path, currentVersion: 1);
        await originalStore.SaveAsync(new TestSettings("Smart", new TestHotkeys("Ctrl+Alt+O")));

        var interruptedWriter = new AtomicFileWriter(
            backupRetentionCount: 3,
            faultInjector: new ThrowBeforeCommitFaultInjector());
        var interruptedStore = CreateStore(path, currentVersion: 1, writer: interruptedWriter);

        await Assert.ThrowsAsync<InjectedAtomicWriteException>(() =>
            interruptedStore.SaveAsync(
                new TestSettings("Guided", new TestHotkeys("Ctrl+Alt+I"))));

        var loaded = await originalStore.LoadAsync();
        Assert.Equal("Smart", loaded.Value.InteractionMode);
        Assert.Equal("Ctrl+Alt+O", loaded.Value.Hotkeys.ContextTrigger);
        Assert.Empty(Directory.EnumerateFiles(temporary.Path, "*.tmp"));
    }

    [Fact]
    public async Task Load_MigratesExplicitlyBacksUpPreviousFileAndRecordsSanitizedResult()
    {
        using var temporary = new TemporaryDirectory();
        var path = System.IO.Path.Combine(temporary.Path, "settings.json");
        var versionOneStore = CreateStore(path, currentVersion: 1);
        await versionOneStore.SaveAsync(
            new TestSettings("Legacy", new TestHotkeys("Ctrl+Alt+O")));

        var migration = new DelegateJsonSettingsMigration(
            sourceVersion: 1,
            targetVersion: 2,
            data =>
            {
                data["interactionMode"] = "Smart";
                return data;
            });
        var journalDirectory = System.IO.Path.Combine(temporary.Path, "migration-journal");
        var versionTwoStore = CreateStore(
            path,
            currentVersion: 2,
            migrations: [migration],
            migrationRecorder: new FileSanitizedMigrationRecorder(journalDirectory));

        var loaded = await versionTwoStore.LoadAsync();

        Assert.Equal(SettingsLoadStatus.Migrated, loaded.Status);
        Assert.Equal("Smart", loaded.Value.InteractionMode);
        Assert.True(loaded.RepairPersisted);
        Assert.True(loaded.MigrationResultRecorded);
        Assert.Single(Directory.EnumerateFiles(temporary.Path, "settings.json.pre-migration.*"));

        var journalPath = Assert.Single(Directory.EnumerateFiles(journalDirectory, "*.json"));
        var journal = await File.ReadAllTextAsync(journalPath);
        Assert.Contains("migration-complete", journal, StringComparison.Ordinal);
        Assert.DoesNotContain("Ctrl+Alt", journal, StringComparison.Ordinal);
        Assert.DoesNotContain("interactionMode", journal, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Load_MalformedJson_QuarantinesFileAndReturnsSafeDefaults()
    {
        using var temporary = new TemporaryDirectory();
        var path = System.IO.Path.Combine(temporary.Path, "settings.json");
        await File.WriteAllTextAsync(path, "{ not valid json");
        var store = CreateStore(path, currentVersion: 1);

        var loaded = await store.LoadAsync();

        Assert.Equal(SettingsLoadStatus.RecoveredCorruptFile, loaded.Status);
        Assert.Equal("Smart", loaded.Value.InteractionMode);
        Assert.Equal("Ctrl+Alt+O", loaded.Value.Hotkeys.ContextTrigger);
        Assert.False(File.Exists(path));
        Assert.Single(Directory.EnumerateFiles(temporary.Path, "settings.json.corrupt.*"));
    }

    [Fact]
    public async Task Load_InvalidSection_PreservesValidSectionAndRepairsOnlyInvalidTopLevelProperty()
    {
        using var temporary = new TemporaryDirectory();
        var path = System.IO.Path.Combine(temporary.Path, "settings.json");
        var store = CreateStore(path, currentVersion: 1);
        await store.SaveAsync(new TestSettings("Guided", new TestHotkeys("Ctrl+Alt+I")));

        var document = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        var data = document["data"]!.AsObject();
        data["hotkeys"] = "invalid-section";
        await File.WriteAllTextAsync(
            path,
            document.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        var recovered = await store.LoadAsync();

        Assert.Equal(SettingsLoadStatus.RecoveredInvalidValues, recovered.Status);
        Assert.Equal("Guided", recovered.Value.InteractionMode);
        Assert.Equal("Ctrl+Alt+O", recovered.Value.Hotkeys.ContextTrigger);
        Assert.Contains("hotkeys", recovered.ResetTopLevelProperties);
        Assert.True(recovered.RepairPersisted);
        Assert.Single(Directory.EnumerateFiles(temporary.Path, "settings.json.pre-recovery.*"));

        var reloaded = await store.LoadAsync();
        Assert.Equal(SettingsLoadStatus.Loaded, reloaded.Status);
        Assert.Equal("Guided", reloaded.Value.InteractionMode);
        Assert.Equal("Ctrl+Alt+O", reloaded.Value.Hotkeys.ContextTrigger);
    }

    [Fact]
    public void StoragePaths_AreDeterministicAndRejectRelativeRoots()
    {
        using var temporary = new TemporaryDirectory();
        var first = CursivisStoragePaths.ForRoot(temporary.Path);
        var second = CursivisStoragePaths.ForRoot(temporary.Path);

        Assert.Equal(first.SettingsFile, second.SettingsFile);
        Assert.Equal(System.IO.Path.Combine(temporary.Path, "settings.json"), first.SettingsFile);
        Assert.Throws<ArgumentException>(() => CursivisStoragePaths.ForRoot("relative-root"));
    }

    private static VersionedJsonSettingsStore<TestSettings> CreateStore(
        string path,
        int currentVersion,
        IEnumerable<IJsonSettingsMigration>? migrations = null,
        ISanitizedMigrationRecorder? migrationRecorder = null,
        AtomicFileWriter? writer = null)
    {
        var codec = new DefaultMergingJsonSettingsCodec<TestSettings>(
            static () => new TestSettings("Smart", new TestHotkeys("Ctrl+Alt+O")),
            static value =>
                !string.IsNullOrWhiteSpace(value.InteractionMode) &&
                !string.IsNullOrWhiteSpace(value.Hotkeys.ContextTrigger));

        return new VersionedJsonSettingsStore<TestSettings>(
            new VersionedJsonSettingsStoreOptions(path, currentVersion),
            codec,
            migrations,
            migrationRecorder,
            writer);
    }

    public sealed record TestSettings(string InteractionMode, TestHotkeys Hotkeys);

    public sealed record TestHotkeys(string ContextTrigger);

    private sealed class ThrowBeforeCommitFaultInjector : IAtomicWriteFaultInjector
    {
        public void Inspect(AtomicWriteStage stage, string destinationFileName)
        {
            _ = destinationFileName;
            if (stage == AtomicWriteStage.BeforeCommit)
            {
                throw new InjectedAtomicWriteException();
            }
        }
    }

    private sealed class InjectedAtomicWriteException : Exception;
}
