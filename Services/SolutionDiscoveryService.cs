using System.Text.RegularExpressions;

namespace LazyNuGet.Services;

public record SolutionInfo(string Name, string FilePath, List<string> ProjectPaths);

/// <summary>
/// Discovers .sln files in a directory tree and extracts the project paths they reference.
/// </summary>
public class SolutionDiscoveryService
{
    public async Task<List<SolutionInfo>> DiscoverSolutionsAsync(string rootPath)
    {
        return await Task.Run(() =>
        {
            var solutions = new List<SolutionInfo>();
            try
            {
                var options = new EnumerationOptions
                {
                    IgnoreInaccessible = true,
                    RecurseSubdirectories = true
                };
                var slnFiles = Directory.GetFiles(rootPath, "*.sln", options);
                foreach (var slnFile in slnFiles)
                {
                    var projectPaths = ParseProjectPaths(slnFile);
                    solutions.Add(new SolutionInfo(
                        Path.GetFileNameWithoutExtension(slnFile),
                        slnFile,
                        projectPaths));
                }
            }
            catch { }
            return solutions;
        });
    }

    private static List<string> ParseProjectPaths(string slnPath)
    {
        var paths = new List<string>();
        try
        {
            var slnDir = Path.GetDirectoryName(slnPath) ?? "";
            var lines = File.ReadAllLines(slnPath);
            foreach (var line in lines)
            {
                // Match: Project("{GUID}") = "Name", "relative\path.csproj", "{GUID}"
                var match = Regex.Match(line,
                    @"Project\(""\{[^}]+\}""\)\s*=\s*""[^""]*""\s*,\s*""([^""]+\.(csproj|fsproj|vbproj))""",
                    RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    var relativePath = match.Groups[1].Value
                        .Replace('\\', Path.DirectorySeparatorChar);
                    var fullPath = Path.GetFullPath(Path.Combine(slnDir, relativePath));
                    paths.Add(fullPath);
                }
            }
        }
        catch { }
        return paths;
    }
}
