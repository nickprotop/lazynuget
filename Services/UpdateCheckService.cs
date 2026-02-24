using System.Net.Http.Json;
using System.Reflection;
using NuGet.Versioning;

namespace LazyNuGet.Services;

/// <summary>
/// Checks NuGet.org for newer versions of LazyNuGet.
/// Best-effort: never throws or blocks the UI.
/// </summary>
public static class UpdateCheckService
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(5)
    };

    public record UpdateCheckResult(bool UpdateAvailable, string LatestVersion, string CurrentVersion);

    public static async Task<UpdateCheckResult> CheckAsync()
    {
        var currentVersionStr = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";

        // Strip build metadata (e.g. "1.0.0+abc123" → "1.0.0")
        var plusIndex = currentVersionStr.IndexOf('+');
        if (plusIndex >= 0)
            currentVersionStr = currentVersionStr[..plusIndex];

        if (!NuGetVersion.TryParse(currentVersionStr, out var currentVersion))
            return new UpdateCheckResult(false, string.Empty, currentVersionStr);

        var url = "https://api.nuget.org/v3-flatcontainer/lazynuget/index.json";
        var response = await Http.GetFromJsonAsync<NuGetVersionIndex>(url);

        if (response?.Versions is not { Count: > 0 })
            return new UpdateCheckResult(false, string.Empty, currentVersionStr);

        // Find the highest stable version
        NuGetVersion? latest = null;
        foreach (var v in response.Versions)
        {
            if (NuGetVersion.TryParse(v, out var parsed) && !parsed.IsPrerelease)
            {
                if (latest == null || parsed > latest)
                    latest = parsed;
            }
        }

        if (latest == null)
            return new UpdateCheckResult(false, string.Empty, currentVersionStr);

        return new UpdateCheckResult(
            latest > currentVersion,
            latest.ToNormalizedString(),
            currentVersion.ToNormalizedString());
    }

    private record NuGetVersionIndex(List<string> Versions);
}
