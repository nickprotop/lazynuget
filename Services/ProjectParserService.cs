using System.Xml.Linq;
using LazyNuGet.Models;

namespace LazyNuGet.Services;

/// <summary>
/// Service for parsing .NET project files and extracting package references
/// </summary>
public class ProjectParserService
{
    /// <summary>
    /// Parse a project file and extract metadata and package references
    /// </summary>
    public async Task<ProjectInfo?> ParseProjectAsync(string projectFilePath)
    {
        return await Task.Run(() =>
        {
            try
            {
                var doc = XDocument.Load(projectFilePath);
                var project = new ProjectInfo
                {
                    Name = Path.GetFileNameWithoutExtension(projectFilePath),
                    FilePath = projectFilePath,
                    LastModified = File.GetLastWriteTime(projectFilePath)
                };

                // Extract target framework
                var targetFramework = doc.Descendants("TargetFramework").FirstOrDefault()?.Value
                                   ?? doc.Descendants("TargetFrameworks").FirstOrDefault()?.Value?.Split(';').FirstOrDefault()
                                   ?? "unknown";
                project.TargetFramework = targetFramework;

                // Extract package references
                var packageReferences = doc.Descendants("PackageReference");
                foreach (var packageRef in packageReferences)
                {
                    var id = packageRef.Attribute("Include")?.Value;
                    var version = packageRef.Attribute("Version")?.Value
                               ?? packageRef.Element("Version")?.Value;

                    if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(version))
                    {
                        project.Packages.Add(new PackageReference
                        {
                            Id = id,
                            Version = version
                        });
                    }
                }

                return project;
            }
            catch (Exception ex)
            {
                // Silently fail - parser service doesn't have logging injected yet
                // Could inject ILogService if needed
                return null;
            }
        });
    }
}
