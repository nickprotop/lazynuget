using System.Xml.Linq;
using NuGet.Versioning;

namespace LazyNuGet.Services;

/// <summary>
/// Migrates PackageReference projects to Central Package Management (CPM) by creating
/// a Directory.Packages.props file and removing inline Version attributes from .csproj files.
/// </summary>
public class CpmMigrationService
{
    private static readonly HashSet<string> _skipDirs = new(StringComparer.OrdinalIgnoreCase)
        { "bin", "obj", ".git", "node_modules" };

    // ── Phase 1: read-only scan ───────────────────────────────────────────────

    /// <summary>
    /// Analyze a folder tree and determine what would be migrated — no files are modified.
    /// </summary>
    public async Task<CpmAnalysisResult> AnalyzeAsync(
        string folderPath,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        progress?.Report("Scanning for .csproj files...");

        var projectsToMigrate = new List<ProjectAnalysis>();
        var projectsSkipped   = new List<ProjectAnalysis>();
        var allVersions       = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        var csprojFiles = EnumerateCsprojFiles(folderPath).ToList();

        foreach (var csprojPath in csprojFiles)
        {
            ct.ThrowIfCancellationRequested();

            var analysis = await Task.Run(() => AnalyzeProject(csprojPath), ct);

            if (analysis.SkipReason != null)
            {
                projectsSkipped.Add(analysis);
            }
            else
            {
                projectsToMigrate.Add(analysis);

                foreach (var (id, version) in analysis.InlineRefs)
                {
                    if (!allVersions.ContainsKey(id))
                        allVersions[id] = new List<string>();
                    allVersions[id].Add(version);
                }
            }
        }

        // Resolve: pick highest version for each package id
        var resolvedVersions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int conflictCount = 0;

        foreach (var (id, versions) in allVersions)
        {
            if (versions.Count > 1) conflictCount++;
            resolvedVersions[id] = ResolveHighestVersion(versions);
        }

        return new CpmAnalysisResult(projectsToMigrate, projectsSkipped, resolvedVersions, conflictCount);
    }

    // ── Phase 2: apply changes using pre-computed analysis ───────────────────

    /// <summary>
    /// Apply migration: creates/updates Directory.Packages.props, removes inline Version
    /// attributes from .csproj files. Creates .csproj.bak backups and rolls back on failure.
    /// </summary>
    public async Task<CpmMigrationResult> MigrateAsync(
        string folderPath,
        CpmAnalysisResult analysis,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var backedUp = new List<string>();
        var propsPath = Path.Combine(folderPath, "Directory.Packages.props");
        bool propsWasNewlyCreated = !File.Exists(propsPath);

        try
        {
            // Step 1: Backup all project files before touching anything
            foreach (var project in analysis.ProjectsToMigrate)
            {
                ct.ThrowIfCancellationRequested();
                File.Copy(project.FilePath, project.FilePath + ".bak", overwrite: true);
                backedUp.Add(project.FilePath);
                progress?.Report($"Backed up {Path.GetFileName(project.FilePath)}");
            }

            // Step 2: Write / update Directory.Packages.props
            progress?.Report("Writing Directory.Packages.props...");
            ct.ThrowIfCancellationRequested();
            await Task.Run(
                () => WriteOrUpdatePropsFile(propsPath, analysis.ResolvedVersions, propsWasNewlyCreated),
                ct);

            // Step 3: Remove inline Version attributes from each project
            var modifiedPaths = new List<string>();
            foreach (var project in analysis.ProjectsToMigrate)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report($"Updating {project.Name}...");
                await Task.Run(() => RemoveInlineVersions(project.FilePath, project.InlineRefs), ct);
                modifiedPaths.Add(project.FilePath);
            }

            progress?.Report($"Done — {analysis.ProjectsToMigrate.Count} project(s) migrated.");

            return new CpmMigrationResult(
                Success:                  true,
                ProjectsMigrated:         analysis.ProjectsToMigrate.Count,
                PackagesCentralized:      analysis.ResolvedVersions.Count,
                VersionConflictsResolved: analysis.VersionConflictsCount,
                ModifiedProjectPaths:     modifiedPaths,
                PropsFilePath:            propsPath,
                Error:                    null);
        }
        catch (Exception ex)
        {
            // Rollback: restore all backed-up project files
            progress?.Report("Rolling back changes...");

            foreach (var path in backedUp)
            {
                var bak = path + ".bak";
                try { if (File.Exists(bak)) File.Copy(bak, path, overwrite: true); }
                catch { /* best-effort */ }
            }

            // Delete props file if we created it (partially written)
            if (propsWasNewlyCreated && File.Exists(propsPath))
            {
                try { File.Delete(propsPath); }
                catch { /* best-effort */ }
            }

            var msg = ex is OperationCanceledException ? "Migration cancelled." : ex.Message;
            return new CpmMigrationResult(
                Success:                  false,
                ProjectsMigrated:         0,
                PackagesCentralized:      0,
                VersionConflictsResolved: 0,
                ModifiedProjectPaths:     new List<string>(),
                PropsFilePath:            null,
                Error:                    msg);
        }
    }

    // ── Internal helpers ──────────────────────────────────────────────────────

    private static IEnumerable<string> EnumerateCsprojFiles(string folderPath)
    {
        var queue = new Queue<string>();
        queue.Enqueue(folderPath);

        while (queue.Count > 0)
        {
            var dir = queue.Dequeue();

            foreach (var file in Directory.GetFiles(dir, "*.csproj"))
                yield return file;

            foreach (var subDir in Directory.GetDirectories(dir))
                if (!_skipDirs.Contains(Path.GetFileName(subDir)))
                    queue.Enqueue(subDir);
        }
    }

    private static ProjectAnalysis AnalyzeProject(string csprojPath)
    {
        var name = Path.GetFileNameWithoutExtension(csprojPath);
        var dir  = Path.GetDirectoryName(csprojPath)!;

        // Skip packages.config projects — they must be migrated to PackageReference first
        if (File.Exists(Path.Combine(dir, "packages.config")))
            return new ProjectAnalysis(csprojPath, name, new List<(string, string)>(),
                "packages.config — migrate first");

        XDocument doc;
        try { doc = XDocument.Load(csprojPath); }
        catch (Exception ex)
            { return new ProjectAnalysis(csprojPath, name, new List<(string, string)>(),
                $"parse error: {ex.Message}"); }

        // Collect PackageReference Include elements (not Update — those are for SDK-implicit packages)
        var pkgRefs = doc.Descendants()
            .Where(e => e.Name.LocalName == "PackageReference"
                     && e.Attribute("Include") != null
                     && e.Attribute("Update") == null)
            .ToList();

        if (!pkgRefs.Any())
            return new ProjectAnalysis(csprojPath, name, new List<(string, string)>(),
                "no PackageReference elements");

        var inlineRefs = new List<(string Id, string Version)>();

        foreach (var pr in pkgRefs)
        {
            // Skip VersionOverride — intentional per-project CPM override, must not be touched
            if (pr.Attribute("VersionOverride") != null)
                continue;

            var id = pr.Attribute("Include")!.Value;

            // Version attribute takes precedence over <Version> child element
            var vAttr = pr.Attribute("Version");
            if (vAttr != null && !string.IsNullOrWhiteSpace(vAttr.Value))
            {
                inlineRefs.Add((id, vAttr.Value));
                continue;
            }

            var vEl = pr.Elements().FirstOrDefault(e => e.Name.LocalName == "Version");
            if (vEl != null && !string.IsNullOrWhiteSpace(vEl.Value))
                inlineRefs.Add((id, vEl.Value));
        }

        return inlineRefs.Count == 0
            ? new ProjectAnalysis(csprojPath, name, new List<(string, string)>(), "already using CPM")
            : new ProjectAnalysis(csprojPath, name, inlineRefs, null);
    }

    private static void WriteOrUpdatePropsFile(
        string propsPath,
        Dictionary<string, string> resolvedVersions,
        bool isNew)
    {
        if (isNew)
        {
            // Create a fresh Directory.Packages.props with all resolved versions
            var doc = new XDocument(
                new XElement("Project",
                    new XElement("PropertyGroup",
                        new XElement("ManagePackageVersionsCentrally", "true")),
                    new XElement("ItemGroup",
                        resolvedVersions.Select(kvp =>
                            new XElement("PackageVersion",
                                new XAttribute("Include", kvp.Key),
                                new XAttribute("Version", kvp.Value))))));
            doc.Save(propsPath);
        }
        else
        {
            // Merge into existing file — preserve whitespace and never downgrade
            var doc = XDocument.Load(propsPath, LoadOptions.PreserveWhitespace);

            foreach (var (id, resolvedVersion) in resolvedVersions)
            {
                var existing = doc.Descendants()
                    .FirstOrDefault(e => e.Name.LocalName == "PackageVersion"
                        && string.Equals(e.Attribute("Include")?.Value, id,
                            StringComparison.OrdinalIgnoreCase));

                if (existing != null)
                {
                    // Only upgrade — never downgrade an existing entry
                    var existingVersion = existing.Attribute("Version")?.Value;
                    if (existingVersion != null && IsVersionHigher(resolvedVersion, existingVersion))
                        existing.SetAttributeValue("Version", resolvedVersion);
                }
                else
                {
                    // Add to first ItemGroup, creating one if necessary
                    var itemGroup = doc.Descendants()
                        .FirstOrDefault(e => e.Name.LocalName == "ItemGroup");

                    if (itemGroup == null)
                    {
                        itemGroup = new XElement("ItemGroup");
                        doc.Root?.Add(itemGroup);
                    }

                    itemGroup.Add(new XElement("PackageVersion",
                        new XAttribute("Include", id),
                        new XAttribute("Version", resolvedVersion)));
                }
            }

            doc.Save(propsPath);
        }
    }

    private static void RemoveInlineVersions(
        string csprojPath,
        List<(string Id, string Version)> inlineRefs)
    {
        var doc    = XDocument.Load(csprojPath, LoadOptions.PreserveWhitespace);
        var refIds = new HashSet<string>(inlineRefs.Select(r => r.Id), StringComparer.OrdinalIgnoreCase);

        foreach (var pr in doc.Descendants()
            .Where(e => e.Name.LocalName == "PackageReference"
                     && refIds.Contains(e.Attribute("Include")?.Value ?? ""))
            .ToList())
        {
            // Remove Version attribute
            pr.Attribute("Version")?.Remove();

            // Remove <Version> child element
            pr.Elements()
                .Where(e => e.Name.LocalName == "Version")
                .ToList()
                .ForEach(e => e.Remove());
        }

        doc.Save(csprojPath);
    }

    private static string ResolveHighestVersion(IEnumerable<string> versions)
    {
        var list = versions.ToList();

        // Version ranges like [1.1.0, 2) fail TryParse — keep verbatim via lexicographic fallback
        var parsed = list
            .Select(v => NuGetVersion.TryParse(v, out var p) ? p : null)
            .Where(v => v != null)
            .ToList();

        return parsed.Count > 0
            ? parsed.Max()!.ToString()
            : list.Max()!;
    }

    private static bool IsVersionHigher(string candidate, string existing)
    {
        if (NuGetVersion.TryParse(candidate, out var cv) && NuGetVersion.TryParse(existing, out var ev))
            return cv > ev;
        return string.Compare(candidate, existing, StringComparison.Ordinal) > 0;
    }
}

// ── Result records ────────────────────────────────────────────────────────────

public record CpmAnalysisResult(
    List<ProjectAnalysis>    ProjectsToMigrate,
    List<ProjectAnalysis>    ProjectsSkipped,
    Dictionary<string, string> ResolvedVersions,
    int                      VersionConflictsCount);

public record ProjectAnalysis(
    string                       FilePath,
    string                       Name,
    List<(string Id, string Version)> InlineRefs,
    string?                      SkipReason);

public record CpmMigrationResult(
    bool          Success,
    int           ProjectsMigrated,
    int           PackagesCentralized,
    int           VersionConflictsResolved,
    List<string>  ModifiedProjectPaths,
    string?       PropsFilePath,
    string?       Error);
