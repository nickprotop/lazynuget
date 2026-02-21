using System.Xml.Linq;

namespace LazyNuGet.Repositories;

/// <summary>
/// Handles reading and writing of Directory.Packages.props files
/// used by Central Package Management (NuGet 6.2+).
/// </summary>
public class CpmRepository
{
    private const string PropsFileName = "Directory.Packages.props";

    /// <summary>
    /// Walk parent directories from the given project file path to find the nearest
    /// Directory.Packages.props file, following the same MSBuild lookup convention.
    /// Returns null if no props file is found.
    /// </summary>
    public static string? FindPropsFile(string projectFilePath)
    {
        var dir = Path.GetDirectoryName(projectFilePath);
        while (!string.IsNullOrEmpty(dir))
        {
            var candidate = Path.Combine(dir, PropsFileName);
            if (File.Exists(candidate))
                return candidate;

            var parent = Path.GetDirectoryName(dir);
            if (parent == dir) break; // filesystem root
            dir = parent;
        }
        return null;
    }

    /// <summary>
    /// Read all &lt;PackageVersion&gt; entries from a Directory.Packages.props file.
    /// Returns a case-insensitive dictionary of packageId → version.
    /// </summary>
    public async Task<Dictionary<string, string>> ReadPackageVersionsAsync(string propsFilePath)
    {
        return await Task.Run(() =>
        {
            try
            {
                var doc = XDocument.Load(propsFilePath);
                var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                foreach (var el in doc.Descendants("PackageVersion"))
                {
                    var id = el.Attribute("Include")?.Value;
                    var version = el.Attribute("Version")?.Value
                               ?? el.Element("Version")?.Value;

                    if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(version))
                        result[id] = version;
                }

                return result;
            }
            catch
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        });
    }

    /// <summary>
    /// Update the Version attribute of a &lt;PackageVersion&gt; entry in Directory.Packages.props.
    /// Preserves all formatting, comments, and other elements in the file.
    /// </summary>
    public async Task UpdatePackageVersionAsync(string propsFilePath, string packageId, string newVersion)
    {
        await Task.Run(() =>
        {
            var doc = XDocument.Load(propsFilePath, LoadOptions.PreserveWhitespace);

            var element = doc.Descendants("PackageVersion")
                .FirstOrDefault(e => string.Equals(
                    e.Attribute("Include")?.Value, packageId,
                    StringComparison.OrdinalIgnoreCase));

            if (element == null)
                throw new InvalidOperationException(
                    $"PackageVersion entry for '{packageId}' not found in {propsFilePath}");

            // Update attribute-style version
            if (element.Attribute("Version") != null)
            {
                element.SetAttributeValue("Version", newVersion);
            }
            else
            {
                // Update element-style version
                var versionEl = element.Element("Version");
                if (versionEl != null)
                    versionEl.Value = newVersion;
                else
                    element.SetAttributeValue("Version", newVersion);
            }

            doc.Save(propsFilePath);
        });
    }

    /// <summary>
    /// Remove a &lt;PackageVersion&gt; entry from Directory.Packages.props entirely.
    /// Use when removing a package from all projects in the solution.
    /// </summary>
    public async Task RemovePackageVersionAsync(string propsFilePath, string packageId)
    {
        await Task.Run(() =>
        {
            var doc = XDocument.Load(propsFilePath, LoadOptions.PreserveWhitespace);

            var element = doc.Descendants("PackageVersion")
                .FirstOrDefault(e => string.Equals(
                    e.Attribute("Include")?.Value, packageId,
                    StringComparison.OrdinalIgnoreCase));

            element?.Remove();

            doc.Save(propsFilePath);
        });
    }
}
