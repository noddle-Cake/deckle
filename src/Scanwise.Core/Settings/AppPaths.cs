namespace Scanwise.Core.Settings;

/// <summary>
/// Per-OS locations for configuration, working data and temp files.
/// Set <c>SCANWISE_HOME</c> to keep everything under one folder (portable mode, tests).
/// </summary>
public sealed class AppPaths
{
    public AppPaths(string configDir, string dataDir)
    {
        ConfigDir = configDir;
        DataDir = dataDir;
    }

    public string ConfigDir { get; }
    public string DataDir { get; }

    public string SettingsFile => Path.Combine(ConfigDir, "settings.json");
    public string ProfilesFile => Path.Combine(ConfigDir, "profiles.json");
    public string SessionDir => Path.Combine(DataDir, "session");
    public string ImageStoreDir => Path.Combine(DataDir, "images");
    public string TempDir => Path.Combine(DataDir, "temp");
    public string TessdataDir => Path.Combine(DataDir, "tessdata");
    public string LogFile => Path.Combine(DataDir, "scanwise.log");

    public static AppPaths Default()
    {
        var portable = Environment.GetEnvironmentVariable("SCANWISE_HOME");
        if (!string.IsNullOrEmpty(portable))
        {
            return new AppPaths(Path.Combine(portable, "config"), Path.Combine(portable, "data"));
        }

        string config, data;
        if (OperatingSystem.IsWindows())
        {
            config = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Scanwise");
            data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Scanwise");
        }
        else if (OperatingSystem.IsMacOS())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            config = data = Path.Combine(home, "Library", "Application Support", "Scanwise");
        }
        else
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            config = Path.Combine(Env("XDG_CONFIG_HOME") ?? Path.Combine(home, ".config"), "scanwise");
            data = Path.Combine(Env("XDG_DATA_HOME") ?? Path.Combine(home, ".local", "share"), "scanwise");
        }
        return new AppPaths(config, data);
    }

    public void EnsureCreated()
    {
        foreach (var dir in new[] { ConfigDir, DataDir, SessionDir, ImageStoreDir, TempDir })
        {
            Directory.CreateDirectory(dir);
        }
    }

    /// <summary>The user's Pictures/Scans folder (or Documents/Scans when there is no Pictures folder).</summary>
    public static string DefaultScanFolder()
    {
        var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        if (string.IsNullOrEmpty(pictures))
        {
            pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }
        if (string.IsNullOrEmpty(pictures))
        {
            pictures = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }
        return Path.Combine(pictures, "Scans");
    }

    private static string? Env(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
