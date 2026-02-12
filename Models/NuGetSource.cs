namespace LazyNuGet.Models;

public enum NuGetSourceOrigin
{
    NuGetConfig,
    LazyNuGetSettings
}

public class NuGetSource
{
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public bool RequiresAuth { get; set; }
    public string? Username { get; set; }
    public string? ClearTextPassword { get; set; }
    public NuGetSourceOrigin Origin { get; set; }
}
