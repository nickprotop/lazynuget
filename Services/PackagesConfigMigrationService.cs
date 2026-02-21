using System.Xml.Linq;

namespace LazyNuGet.Services;

/// <summary>
/// Migrates legacy packages.config projects to the modern PackageReference format.
/// </summary>
public class PackagesConfigMigrationService
{
    /// <summary>
    /// Migrate a single project from packages.config to PackageReference.
    /// Creates a .csproj.bak backup before modifying the project file.
    /// On failure, restores the project file from backup.
    /// </summary>
    public async Task<MigrationResult> MigrateProjectAsync(
        string projectFilePath,
        CancellationToken ct = default)
    {
        var backupPath = projectFilePath + ".bak";

        try
        {
            // Step 1: Locate packages.config
            var projectDir         = Path.GetDirectoryName(projectFilePath)!;
            var packagesConfigPath = Path.Combine(projectDir, "packages.config");

            if (!File.Exists(packagesConfigPath))
                return new MigrationResult(projectFilePath, false, 0,
                    "packages.config not found next to project file");

            // Step 2: Parse packages from packages.config
            var packages = await Task.Run(() => ParsePackagesConfig(packagesConfigPath), ct);

            // Step 3: Check if already using PackageReference
            // Use LocalName queries to handle both modern (no namespace) and legacy
            // (MSBuild namespace: http://schemas.microsoft.com/developer/msbuild/2003) csproj files.
            var csprojContent = await File.ReadAllTextAsync(projectFilePath, ct);
            var csprojDoc = XDocument.Parse(csprojContent);
            if (csprojDoc.Descendants().Any(e => e.Name.LocalName == "PackageReference"))
                return new MigrationResult(projectFilePath, false, 0,
                    "Project already uses PackageReference");

            // Step 4: Create backup
            File.Copy(projectFilePath, backupPath, overwrite: true);

            // Step 5–9: Modify project file and delete packages.config
            await Task.Run(() =>
            {
                // Detect MSBuild namespace (legacy projects use xmlns="http://...")
                var ns = csprojDoc.Root?.Name.Namespace ?? XNamespace.None;

                // Step 6: Remove <Reference> elements whose <HintPath> points into the packages folder
                var refsToRemove = csprojDoc.Descendants()
                    .Where(e => e.Name.LocalName == "Reference")
                    .Where(r =>
                    {
                        var hintPathEl = r.Elements().FirstOrDefault(e => e.Name.LocalName == "HintPath");
                        var hintPath = hintPathEl?.Value ?? string.Empty;
                        return hintPath.IndexOf(@"packages\", StringComparison.OrdinalIgnoreCase) >= 0
                            || hintPath.IndexOf("packages/", StringComparison.OrdinalIgnoreCase) >= 0;
                    })
                    .ToList();

                foreach (var r in refsToRemove)
                {
                    // Remove the parent <ItemGroup> if it becomes empty after removal
                    var parent = r.Parent;
                    r.Remove();
                    if (parent?.Name.LocalName == "ItemGroup" && !parent.Elements().Any())
                        parent.Remove();
                }

                // Step 7: Remove NuGet-related <Import> elements
                var importsToRemove = csprojDoc.Descendants()
                    .Where(e => e.Name.LocalName == "Import")
                    .Where(i =>
                    {
                        var project = i.Attribute("Project")?.Value ?? string.Empty;
                        return project.IndexOf(@"\.nuget\", StringComparison.OrdinalIgnoreCase) >= 0
                            || project.IndexOf("/.nuget/", StringComparison.OrdinalIgnoreCase) >= 0
                            || (project.IndexOf("packages", StringComparison.OrdinalIgnoreCase) >= 0
                                && project.EndsWith(".targets", StringComparison.OrdinalIgnoreCase));
                    })
                    .ToList();

                foreach (var imp in importsToRemove)
                    imp.Remove();

                // Step 8: Add <PackageReference> elements
                if (packages.Count > 0)
                {
                    // Find an existing ItemGroup to append to, or create one
                    var existingGroup = csprojDoc.Descendants()
                        .FirstOrDefault(e => e.Name.LocalName == "ItemGroup");
                    XElement itemGroup;
                    if (existingGroup != null)
                    {
                        itemGroup = existingGroup;
                    }
                    else
                    {
                        // Inherit namespace from root to keep document consistent
                        itemGroup = new XElement(ns + "ItemGroup");
                        csprojDoc.Root?.Add(itemGroup);
                    }

                    foreach (var (id, version) in packages)
                    {
                        itemGroup.Add(new XElement(ns + "PackageReference",
                            new XAttribute("Include", id),
                            new XAttribute("Version", version)));
                    }
                }

                // Step 9: Save the modified .csproj
                csprojDoc.Save(projectFilePath);
            }, ct);

            // Step 10: Delete packages.config
            File.Delete(packagesConfigPath);

            // Clean up backup only after full success
            // (leave .bak so user can verify; they can delete manually)

            // Step 11: Return success
            return new MigrationResult(projectFilePath, true, packages.Count, null);
        }
        catch (Exception ex)
        {
            // Step 12: Restore from backup on any failure
            try
            {
                if (File.Exists(backupPath))
                    File.Copy(backupPath, projectFilePath, overwrite: true);
            }
            catch
            {
                // Best-effort restore — if this fails too, the backup still exists
            }

            return new MigrationResult(projectFilePath, false, 0, ex.Message);
        }
    }

    /// <summary>
    /// Migrate all packages.config projects found within a folder tree.
    /// </summary>
    public async Task<List<MigrationResult>> MigrateAllInFolderAsync(
        string folderPath,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var results = new List<MigrationResult>();

        var configFiles = Directory.GetFiles(folderPath, "packages.config",
            SearchOption.AllDirectories);

        foreach (var configFile in configFiles)
        {
            ct.ThrowIfCancellationRequested();

            var dir = Path.GetDirectoryName(configFile)!;
            var projectFile = FindProjectFile(dir);

            if (projectFile == null)
            {
                progress?.Report($"Skipping {configFile} — no project file found in directory");
                results.Add(new MigrationResult(configFile, false, 0,
                    "No .csproj/.fsproj/.vbproj found in same directory"));
                continue;
            }

            progress?.Report($"Migrating {Path.GetFileName(projectFile)}...");
            var result = await MigrateProjectAsync(projectFile, ct);
            results.Add(result);
        }

        return results;
    }

    private static string? FindProjectFile(string directory)
    {
        var patterns = new[] { "*.csproj", "*.fsproj", "*.vbproj" };
        foreach (var pattern in patterns)
        {
            var files = Directory.GetFiles(directory, pattern);
            if (files.Length > 0) return files[0];
        }
        return null;
    }

    private static List<(string Id, string Version)> ParsePackagesConfig(string path)
    {
        var doc = XDocument.Load(path);
        return doc.Descendants("package")
            .Select(pkg => new
            {
                Id      = pkg.Attribute("id")?.Value,
                Version = pkg.Attribute("version")?.Value
            })
            .Where(p => !string.IsNullOrEmpty(p.Id) && !string.IsNullOrEmpty(p.Version))
            .Select(p => (p.Id!, p.Version!))
            .ToList();
    }
}

/// <summary>
/// Result of a packages.config migration operation.
/// </summary>
public record MigrationResult(
    string  ProjectPath,
    bool    Success,
    int     PackagesMigrated,
    string? Error);
