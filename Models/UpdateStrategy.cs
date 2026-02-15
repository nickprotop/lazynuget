namespace LazyNuGet.Models;

/// <summary>
/// Strategy for updating packages based on semantic versioning
/// </summary>
public enum UpdateStrategy
{
    UpdateAllToLatest = 0,
    MinorAndPatchOnly = 1,
    PatchOnly = 2
}

public static class UpdateStrategyExtensions
{
    public static string GetDisplayName(this UpdateStrategy strategy) => strategy switch
    {
        UpdateStrategy.UpdateAllToLatest => "Update All to Latest",
        UpdateStrategy.MinorAndPatchOnly => "Minor & Patch Only",
        UpdateStrategy.PatchOnly => "Patch Only",
        _ => "Unknown"
    };

    public static string GetDescription(this UpdateStrategy strategy) => strategy switch
    {
        UpdateStrategy.UpdateAllToLatest => "Update all packages to their latest versions (including major version changes)",
        UpdateStrategy.MinorAndPatchOnly => "Skip major version bumps (e.g., 1.x → 2.x). Allows minor and patch updates.",
        UpdateStrategy.PatchOnly => "Skip major and minor bumps. Only apply patch updates (e.g., 1.2.3 → 1.2.7).",
        _ => ""
    };

    public static string GetShortcutKey(this UpdateStrategy strategy) => strategy switch
    {
        UpdateStrategy.UpdateAllToLatest => "F1",
        UpdateStrategy.MinorAndPatchOnly => "F2",
        UpdateStrategy.PatchOnly => "F3",
        _ => ""
    };
}
