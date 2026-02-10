namespace LazyNuGet.Services;

/// <summary>
/// Service for discovering .NET project files recursively
/// </summary>
public class ProjectDiscoveryService
{
    private static readonly string[] ProjectExtensions = { ".csproj", ".fsproj", ".vbproj" };

    /// <summary>
    /// Discover all .NET project files in a directory recursively
    /// </summary>
    public async Task<List<string>> DiscoverProjectsAsync(string folderPath)
    {
        return await Task.Run(() =>
        {
            var projects = new List<string>();

            if (!Directory.Exists(folderPath))
                return projects;

            try
            {
                // Search recursively for project files
                foreach (var extension in ProjectExtensions)
                {
                    var files = Directory.GetFiles(folderPath, $"*{extension}", SearchOption.AllDirectories);
                    projects.AddRange(files);
                }

                // Filter out bin/obj directories
                projects = projects
                    .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                               !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                    .OrderBy(p => p)
                    .ToList();

                return projects;
            }
            catch (UnauthorizedAccessException)
            {
                // Skip directories we don't have access to
                return projects;
            }
        });
    }
}
