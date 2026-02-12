using System.Xml.Linq;
using LazyNuGet.Models;
using SharpConsoleUI.Logging;

namespace LazyNuGet.Services;

/// <summary>
/// Discovers and parses the NuGet.config hierarchy to resolve effective package sources.
/// Walks up from the project directory and merges with user-level config.
/// </summary>
public class NuGetConfigService
{
    private readonly ILogService? _logService;

    public NuGetConfigService(ILogService? logService = null)
    {
        _logService = logService;
    }

    /// <summary>
    /// Get the effective NuGet sources for a given project directory.
    /// Merges configs from project directory up through parents to user-level config.
    /// </summary>
    public List<NuGetSource> GetEffectiveSources(string projectDirectory)
    {
        var configFiles = DiscoverConfigFiles(projectDirectory);
        var sources = new Dictionary<string, NuGetSource>(StringComparer.OrdinalIgnoreCase);
        var disabledSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var credentials = new Dictionary<string, (string? user, string? pass)>(StringComparer.OrdinalIgnoreCase);

        // Process configs from farthest (user-level) to closest (project-level)
        // Closer configs override farther ones
        foreach (var configFile in configFiles.AsEnumerable().Reverse())
        {
            try
            {
                ParseConfigFile(configFile, sources, disabledSources, credentials);
            }
            catch (Exception ex)
            {
                _logService?.LogWarning($"Could not parse NuGet.config at {configFile}: {ex.Message}", "NuGetConfig");
            }
        }

        // Apply disabled status and credentials
        foreach (var source in sources.Values)
        {
            if (disabledSources.Contains(source.Name))
            {
                source.IsEnabled = false;
            }

            if (credentials.TryGetValue(source.Name, out var cred))
            {
                source.Username = cred.user;
                source.ClearTextPassword = cred.pass;
                source.RequiresAuth = !string.IsNullOrEmpty(cred.user);
            }
        }

        return sources.Values.ToList();
    }

    /// <summary>
    /// Get the paths of all discovered NuGet.config files (for display in Settings modal)
    /// </summary>
    public List<string> GetConfigFilePaths(string projectDirectory)
    {
        return DiscoverConfigFiles(projectDirectory);
    }

    /// <summary>
    /// Discover NuGet.config files from project directory up to user-level.
    /// Returns in order from closest (project-level) to farthest (user-level).
    /// </summary>
    private List<string> DiscoverConfigFiles(string projectDirectory)
    {
        var configs = new List<string>();

        // Walk up from project directory looking for NuGet.config
        var dir = new DirectoryInfo(projectDirectory);
        while (dir != null)
        {
            var configPath = FindNuGetConfig(dir.FullName);
            if (configPath != null)
            {
                configs.Add(configPath);
            }
            dir = dir.Parent;
        }

        // Add user-level config
        var userConfig = GetUserLevelConfigPath();
        if (userConfig != null && File.Exists(userConfig) && !configs.Contains(userConfig, StringComparer.OrdinalIgnoreCase))
        {
            configs.Add(userConfig);
        }

        return configs;
    }

    /// <summary>
    /// Find NuGet.config in a directory (case-insensitive)
    /// </summary>
    private static string? FindNuGetConfig(string directory)
    {
        try
        {
            var files = Directory.GetFiles(directory, "nuget.config", SearchOption.TopDirectoryOnly);
            return files.FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Get the user-level NuGet.Config path
    /// </summary>
    private static string? GetUserLevelConfigPath()
    {
        if (OperatingSystem.IsWindows())
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "NuGet", "NuGet.Config");
        }
        else
        {
            // Linux and macOS
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, ".nuget", "NuGet", "NuGet.Config");
        }
    }

    private void ParseConfigFile(
        string configPath,
        Dictionary<string, NuGetSource> sources,
        HashSet<string> disabledSources,
        Dictionary<string, (string? user, string? pass)> credentials)
    {
        var doc = XDocument.Load(configPath);
        var root = doc.Root;
        if (root == null) return;

        // Parse <packageSources>
        var packageSources = root.Element("packageSources");
        if (packageSources != null)
        {
            // Handle <clear /> — removes all previously defined sources
            if (packageSources.Elements("clear").Any())
            {
                sources.Clear();
            }

            foreach (var add in packageSources.Elements("add"))
            {
                var key = add.Attribute("key")?.Value;
                var value = add.Attribute("value")?.Value;
                if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(value))
                {
                    sources[key] = new NuGetSource
                    {
                        Name = key,
                        Url = value,
                        IsEnabled = true,
                        Origin = NuGetSourceOrigin.NuGetConfig
                    };
                }
            }

            // Handle <remove>
            foreach (var remove in packageSources.Elements("remove"))
            {
                var key = remove.Attribute("key")?.Value;
                if (!string.IsNullOrEmpty(key))
                {
                    sources.Remove(key);
                }
            }
        }

        // Parse <disabledPackageSources>
        var disabled = root.Element("disabledPackageSources");
        if (disabled != null)
        {
            foreach (var add in disabled.Elements("add"))
            {
                var key = add.Attribute("key")?.Value;
                var value = add.Attribute("value")?.Value;
                if (!string.IsNullOrEmpty(key))
                {
                    if (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
                    {
                        disabledSources.Add(key);
                    }
                    else
                    {
                        disabledSources.Remove(key);
                    }
                }
            }
        }

        // Parse <packageSourceCredentials>
        var creds = root.Element("packageSourceCredentials");
        if (creds != null)
        {
            foreach (var sourceElement in creds.Elements())
            {
                var sourceName = sourceElement.Name.LocalName;
                // NuGet.config uses XML-safe names with dots replaced by underscores
                // but the element name typically matches the source key
                string? username = null;
                string? password = null;

                foreach (var add in sourceElement.Elements("add"))
                {
                    var key = add.Attribute("key")?.Value;
                    var value = add.Attribute("value")?.Value;

                    if (string.Equals(key, "Username", StringComparison.OrdinalIgnoreCase))
                        username = value;
                    else if (string.Equals(key, "ClearTextPassword", StringComparison.OrdinalIgnoreCase))
                        password = value;
                }

                if (username != null || password != null)
                {
                    credentials[sourceName] = (username, password);
                }
            }
        }
    }
}
