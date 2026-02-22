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
            // Use LocalName-based queries so MSBuild-namespaced files (e.g. legacy .csproj with
            // xmlns="http://schemas.microsoft.com/developer/msbuild/2003") are handled correctly.
            var singleTf = Desc(doc, "TargetFramework").FirstOrDefault()?.Value;
            var multiTf  = Desc(doc, "TargetFrameworks").FirstOrDefault()?.Value;
            var frameworks = singleTf != null
                ? new List<string> { singleTf }
                : (multiTf?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                         .ToList() ?? new List<string>());
            projectData.TargetFrameworks = frameworks;
            projectData.TargetFramework  = frameworks.FirstOrDefault() ?? "unknown";

            // Detect packages.config (legacy .NET Framework projects)
            var projectDir         = Path.GetDirectoryName(projectFilePath)!;
            var packagesConfigPath = Path.Combine(projectDir, "packages.config");

            if (File.Exists(packagesConfigPath))
            {
                projectData.IsPackagesConfig   = true;
                projectData.PackagesConfigPath = packagesConfigPath;
                projectData.PackageReferences  = ParsePackagesConfig(packagesConfigPath);
                return projectData;
            }

            // Detect Central Package Management.
            // ManagePackageVersionsCentrally may live in the .csproj itself OR in a
            // Directory.Packages.props file in a parent directory (the standard MSBuild pattern).
            bool isCpm = Desc(doc, "ManagePackageVersionsCentrally")
                .Any(e => e.Value.Equals("true", StringComparison.OrdinalIgnoreCase));

            Dictionary<string, string> centralVersions = new(StringComparer.OrdinalIgnoreCase);
            string? propsFilePath = CpmRepository.FindPropsFile(projectFilePath);

            if (!isCpm && propsFilePath != null)
            {
                var propsDoc = XDocument.Load(propsFilePath);
                isCpm = propsDoc.Descendants("ManagePackageVersionsCentrally")
                    .Any(e => e.Value.Equals("true", StringComparison.OrdinalIgnoreCase));
            }

            if (isCpm && propsFilePath != null)
                centralVersions = await _cpmRepository.ReadPackageVersionsAsync(propsFilePath);

            projectData.IsCpmEnabled = isCpm;
            projectData.PropsFilePath = propsFilePath;

            // Extract package references — supports inline, CPM central, and VersionOverride
            foreach (var packageRef in Desc(doc, "PackageReference"))
            {
                var id = packageRef.Attribute("Include")?.Value;
                if (string.IsNullOrEmpty(id)) continue;

                var inlineVersion    = packageRef.Attribute("Version")?.Value
                                    ?? packageRef.Elements().FirstOrDefault(e => e.Name.LocalName == "Version")?.Value;
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
    /// Parse a packages.config file and return a list of package references.
    /// Returns an empty list on malformed XML.
    /// </summary>
    /// <summary>
    /// Descendant query by LocalName, ignoring XML namespace.
    /// Required for MSBuild-namespaced .csproj files (xmlns="http://schemas.microsoft.com/developer/msbuild/2003").
    /// </summary>
    private static IEnumerable<XElement> Desc(XDocument doc, string localName) =>
        doc.Descendants().Where(e => e.Name.LocalName == localName);

    private static List<PackageReferenceData> ParsePackagesConfig(string path)
    {
        try
        {
            var doc = XDocument.Load(path);
            return doc.Descendants("package")
                .Select(pkg => new
                {
                    Id      = pkg.Attribute("id")?.Value,
                    Version = pkg.Attribute("version")?.Value
                })
                .Where(p => !string.IsNullOrEmpty(p.Id) && !string.IsNullOrEmpty(p.Version))
                .Select(p => new PackageReferenceData
                {
                    Id            = p.Id!,
                    Version       = p.Version!,
                    VersionSource = VersionSource.Inline
                })
                .ToList();
        }
        catch
        {
            return new List<PackageReferenceData>();
        }
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
    public bool IsPackagesConfig { get; set; }
    public string? PackagesConfigPath { get; set; }
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
