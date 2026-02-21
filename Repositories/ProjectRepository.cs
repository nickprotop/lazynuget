using System.Xml.Linq;
using LazyNuGet.Models;

namespace LazyNuGet.Repositories;

/// <summary>
/// Repository for accessing and parsing .NET project files.
/// This is the data access layer - handles all MSBuild XML I/O.
/// Supports both inline package versions and Central Package Management (CPM).
/// </summary>
public class ProjectRepository
{
    private readonly CpmRepository _cpmRepository = new();

    /// <summary>
    /// Read and parse a project file from disk.
    /// Detects Central Package Management and resolves versions from Directory.Packages.props.
    /// </summary>
    public async Task<ProjectFileData?> ReadProjectFileAsync(string projectFilePath)
    {
        try
        {
            var doc = await Task.Run(() => XDocument.Load(projectFilePath));

            var projectData = new ProjectFileData
            {
                FilePath = projectFilePath,
                Name = Path.GetFileNameWithoutExtension(projectFilePath),
                LastModified = File.GetLastWriteTime(projectFilePath)
            };

            // Extract target framework(s)
            var singleTf = doc.Descendants("TargetFramework").FirstOrDefault()?.Value;
            var multiTf  = doc.Descendants("TargetFrameworks").FirstOrDefault()?.Value;
            var frameworks = singleTf != null
                ? new List<string> { singleTf }
                : (multiTf?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                         .ToList() ?? new List<string>());
            projectData.TargetFrameworks = frameworks;
            projectData.TargetFramework  = frameworks.FirstOrDefault() ?? "unknown";

            // Detect Central Package Management
            bool isCpm = doc.Descendants("ManagePackageVersionsCentrally")
                .Any(e => e.Value.Equals("true", StringComparison.OrdinalIgnoreCase));

            Dictionary<string, string> centralVersions = new(StringComparer.OrdinalIgnoreCase);
            string? propsFilePath = null;

            if (isCpm)
            {
                propsFilePath = CpmRepository.FindPropsFile(projectFilePath);
                if (propsFilePath != null)
                    centralVersions = await _cpmRepository.ReadPackageVersionsAsync(propsFilePath);
            }

            projectData.IsCpmEnabled = isCpm;
            projectData.PropsFilePath = propsFilePath;

            // Extract package references — supports inline, CPM central, and VersionOverride
            foreach (var packageRef in doc.Descendants("PackageReference"))
            {
                var id = packageRef.Attribute("Include")?.Value;
                if (string.IsNullOrEmpty(id)) continue;

                var inlineVersion    = packageRef.Attribute("Version")?.Value
                                    ?? packageRef.Element("Version")?.Value;
                var overrideVersion  = packageRef.Attribute("VersionOverride")?.Value;

                string? version;
                VersionSource source;

                if (!string.IsNullOrEmpty(overrideVersion))
                {
                    // Project explicitly overrides the central version
                    version = overrideVersion;
                    source  = VersionSource.Override;
                }
                else if (!string.IsNullOrEmpty(inlineVersion))
                {
                    // Normal inline version in the project file
                    version = inlineVersion;
                    source  = VersionSource.Inline;
                }
                else if (centralVersions.TryGetValue(id, out var central))
                {
                    // Version comes from Directory.Packages.props
                    version = central;
                    source  = VersionSource.Central;
                }
                else
                {
                    // No version found anywhere — malformed reference, skip
                    continue;
                }

                projectData.PackageReferences.Add(new PackageReferenceData
                {
                    Id            = id,
                    Version       = version,
                    VersionSource = source,
                    PropsFilePath = source != VersionSource.Inline ? propsFilePath : null
                });
            }

            return projectData;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Discover all .csproj, .fsproj, .vbproj files in a directory tree
    /// </summary>
    public async Task<List<string>> DiscoverProjectFilesAsync(string rootPath)
    {
        return await Task.Run(() =>
        {
            try
            {
                var patterns = new[] { "*.csproj", "*.fsproj", "*.vbproj" };
                var projectFiles = new List<string>();

                // Use EnumerationOptions to skip inaccessible directories instead of failing
                var options = new EnumerationOptions
                {
                    IgnoreInaccessible = true,
                    RecurseSubdirectories = true
                };

                foreach (var pattern in patterns)
                {
                    projectFiles.AddRange(
                        Directory.GetFiles(rootPath, pattern, options));
                }

                return projectFiles;
            }
            catch
            {
                return new List<string>();
            }
        });
    }

    /// <summary>
    /// Check if a file exists
    /// </summary>
    public bool ProjectFileExists(string filePath)
    {
        return File.Exists(filePath);
    }

    /// <summary>
    /// Get file last modified time
    /// </summary>
    public DateTime GetLastModifiedTime(string filePath)
    {
        return File.GetLastWriteTime(filePath);
    }
}

/// <summary>
/// Raw data read from a project file
/// </summary>
public class ProjectFileData
{
    public string FilePath { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string TargetFramework { get; set; } = string.Empty;
    public List<string> TargetFrameworks { get; set; } = new();
    public DateTime LastModified { get; set; }
    public List<PackageReferenceData> PackageReferences { get; set; } = new();
    public bool IsCpmEnabled { get; set; }
    public string? PropsFilePath { get; set; }
}

/// <summary>
/// Raw package reference data from project file
/// </summary>
public class PackageReferenceData
{
    public string Id { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public VersionSource VersionSource { get; set; } = VersionSource.Inline;
    public string? PropsFilePath { get; set; }
}
