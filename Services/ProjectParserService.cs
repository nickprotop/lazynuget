using LazyNuGet.Models;
using LazyNuGet.Repositories;
using SharpConsoleUI.Logging;

namespace LazyNuGet.Services;

/// <summary>
/// Service for parsing .NET project files - provides business logic on top of ProjectRepository.
/// Converts raw project data into rich ProjectInfo models.
/// </summary>
public class ProjectParserService
{
    private readonly ProjectRepository _repository;
    private readonly ILogService? _logService;

    public ProjectParserService(ILogService? logService = null)
    {
        _repository = new ProjectRepository();
        _logService = logService;
    }

    /// <summary>
    /// Parse a project file and extract metadata and package references
    /// </summary>
    public async Task<ProjectInfo?> ParseProjectAsync(string projectFilePath)
    {
        try
        {
            var projectData = await _repository.ReadProjectFileAsync(projectFilePath);
            if (projectData == null)
            {
                _logService?.LogError($"Failed to parse project file: {projectFilePath}", null, "Parser");
                return null;
            }

            var project = new ProjectInfo
            {
                Name = projectData.Name,
                FilePath = projectData.FilePath,
                LastModified = projectData.LastModified,
                TargetFramework = projectData.TargetFramework,
                TargetFrameworks = projectData.TargetFrameworks
            };

            // Convert package references to PackageReference models
            foreach (var pkgRef in projectData.PackageReferences)
            {
                project.Packages.Add(new PackageReference
                {
                    Id = pkgRef.Id,
                    Version = pkgRef.Version
                });
            }

            return project;
        }
        catch (Exception ex)
        {
            _logService?.LogError($"Failed to parse project file: {projectFilePath}", ex, "Parser");
            return null;
        }
    }
}
