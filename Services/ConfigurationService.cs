using System.Text.Json;

namespace LazyNuGet.Services;

public class CustomNuGetSource
{
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
}

public class LazyNuGetSettings
{
    public string? LastFolderPath { get; set; }
    public List<string> RecentFolders { get; set; } = new();
    public List<CustomNuGetSource> CustomSources { get; set; } = new();
    public Dictionary<string, bool> SourceOverrides { get; set; } = new();
    public bool ShowWelcomeOnStartup { get; set; } = true;
}

/// <summary>
/// Persists user settings (last folder, recent folders) to a JSON file
/// </summary>
public class ConfigurationService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static string ConfigDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "LazyNuGet");

    private static string ConfigFile => Path.Combine(ConfigDir, "settings.json");

    public LazyNuGetSettings Load()
    {
        try
        {
            if (!File.Exists(ConfigFile))
                return new LazyNuGetSettings();

            var json = File.ReadAllText(ConfigFile);
            return JsonSerializer.Deserialize<LazyNuGetSettings>(json, JsonOptions)
                   ?? new LazyNuGetSettings();
        }
        catch
        {
            return new LazyNuGetSettings();
        }
    }

    public void Save(LazyNuGetSettings settings)
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(ConfigFile, json);
        }
        catch
        {
            // Silently fail — settings persistence is non-critical
        }
    }

    /// <summary>
    /// Convenience: update LastFolderPath and add to RecentFolders in one call
    /// </summary>
    public void TrackFolder(string folderPath)
    {
        var settings = Load();
        settings.LastFolderPath = folderPath;

        // Add to recent folders (most recent first), deduplicate, cap at 10
        settings.RecentFolders.Remove(folderPath);
        settings.RecentFolders.Insert(0, folderPath);
        if (settings.RecentFolders.Count > 10)
            settings.RecentFolders.RemoveRange(10, settings.RecentFolders.Count - 10);

        Save(settings);
    }
}
