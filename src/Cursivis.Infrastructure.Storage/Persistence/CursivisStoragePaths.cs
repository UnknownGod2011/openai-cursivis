namespace Cursivis.Infrastructure.Storage.Persistence;

public sealed class CursivisStoragePaths
{
    private const string DefaultApplicationDirectoryName = "Cursivis";

    private CursivisStoragePaths(string rootDirectory)
    {
        RootDirectory = Path.GetFullPath(rootDirectory);
    }

    public string RootDirectory { get; }

    public string SettingsFile => Path.Combine(RootDirectory, "settings.json");

    public string QuickTaskFile => Path.Combine(RootDirectory, "quick-task.json");

    public string HotkeysFile => Path.Combine(RootDirectory, "hotkeys.json");

    public string MemoryFile => Path.Combine(RootDirectory, "memory.json");

    public string SecretsDirectory => Path.Combine(RootDirectory, "secrets");

    public string MigrationJournalDirectory => Path.Combine(RootDirectory, "migration-journal");

    public string LogsDirectory => Path.Combine(RootDirectory, "logs");

    public string OverlayPlacementFile => Path.Combine(RootDirectory, "overlay-placement.json");

    public string ResultPanelSizeFile => Path.Combine(RootDirectory, "result-panel-size.json");

    public string ResultPanelPlacementFile => Path.Combine(RootDirectory, "result-panel-placement.json");

    public static CursivisStoragePaths ForCurrentUser()
    {
        var localApplicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.DoNotVerify);

        if (string.IsNullOrWhiteSpace(localApplicationData))
        {
            throw new InvalidOperationException("Current-user local application data is unavailable.");
        }

        return new CursivisStoragePaths(
            Path.Combine(localApplicationData, DefaultApplicationDirectoryName));
    }

    public static CursivisStoragePaths ForRoot(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);

        if (!Path.IsPathFullyQualified(rootDirectory))
        {
            throw new ArgumentException("The storage root must be an absolute path.", nameof(rootDirectory));
        }

        return new CursivisStoragePaths(rootDirectory);
    }
}
