using System.Xml.Linq;
using LazyNuGet.Models;

namespace LazyNuGet.Repositories;

/// <summary>
/// Repository for accessing and parsing .NET project files.
/// This is the data access layer - handles all MSBuild XML I/O.
/// </summary>
public class ProjectRepository
{
    /// <summary>
    /// Read and parse a project file from disk
    /// </summary>
    public async Task<ProjectFileData?> ReadProjectFileAsync(string projectFilePath)
    {
        return await Task.Run(() =>
        {
            try
            {
                var doc = XDocument.Load(projectFilePath);
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

                // Extract package references
                var packageReferences = doc.Descendants("PackageReference");
                foreach (var packageRef in packageReferences)
                {
                    var id = packageRef.Attribute("Include")?.Value;
                    var version = packageRef.Attribute("Version")?.Value
                               ?? packageRef.Element("Version")?.Value;

                    if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(version))
                    {
                        projectData.PackageReferences.Add(new PackageReferenceData
                        {
                            Id = id,
                            Version = version
                        });
                    }
                }

                return projectData;
            }
            catch
            {
                return null;
            }
        });
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
}

/// <summary>
/// Raw package reference data from project file
/// </summary>
public class PackageReferenceData
{
    public string Id { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
}
