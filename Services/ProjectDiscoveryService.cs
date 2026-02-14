using LazyNuGet.Repositories;

namespace LazyNuGet.Services;

/// <summary>
/// Service for discovering .NET project files - provides business logic on top of ProjectRepository.
/// Applies filtering rules (e.g., exclude bin/obj directories).
/// </summary>
public class ProjectDiscoveryService
{
    private readonly ProjectRepository _repository;

    public ProjectDiscoveryService()
    {
        _repository = new ProjectRepository();
    }

    /// <summary>
    /// Discover all .NET project files in a directory recursively
    /// </summary>
    public async Task<List<string>> DiscoverProjectsAsync(string folderPath)
    {
        if (!Directory.Exists(folderPath))
            return new List<string>();

        try
        {
            var projects = await _repository.DiscoverProjectFilesAsync(folderPath);

            // Apply business rule: Filter out bin/obj directories
            return projects
                .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                           !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                .OrderBy(p => p)
                .ToList();
        }
        catch (UnauthorizedAccessException)
        {
            // Skip directories we don't have access to
            return new List<string>();
        }
    }
}
