using LazyNuGet.Models;

namespace LazyNuGet.Tests.Helpers;

/// <summary>
/// Factory methods for creating test data objects.
/// </summary>
public static class SampleDataBuilder
{
    public static PackageReference CreatePackageReference(
        string id = "Newtonsoft.Json",
        string version = "13.0.1",
        string? latestVersion = null,
        bool hasVulnerability = false)
    {
        return new PackageReference
        {
            Id = id,
            Version = version,
            LatestVersion = latestVersion,
            HasVulnerability = hasVulnerability
        };
    }

    public static ProjectInfo CreateProjectInfo(
        string name = "TestProject",
        string filePath = "/src/TestProject/TestProject.csproj",
        string targetFramework = "net9.0",
        List<PackageReference>? packages = null)
    {
        return new ProjectInfo
        {
            Name = name,
            FilePath = filePath,
            TargetFramework = targetFramework,
            Packages = packages ?? new List<PackageReference>(),
            LastModified = new DateTime(2025, 1, 1)
        };
    }

    public static NuGetPackage CreateNuGetPackage(
        string id = "Newtonsoft.Json",
        string version = "13.0.3",
        string description = "Popular JSON framework",
        long totalDownloads = 1_500_000_000,
        long? packageSize = null,
        List<string>? versions = null)
    {
        return new NuGetPackage
        {
            Id = id,
            Version = version,
            Description = description,
            TotalDownloads = totalDownloads,
            PackageSize = packageSize,
            Versions = versions ?? new List<string> { "13.0.3", "13.0.2", "13.0.1", "12.0.3" },
            Authors = new List<string> { "James Newton-King" },
            Tags = new List<string> { "json", "serialization" },
            Published = new DateTime(2023, 3, 8)
        };
    }

    public static OperationHistoryEntry CreateHistoryEntry(
        OperationType type = OperationType.Update,
        string projectName = "TestProject",
        bool success = true,
        string? errorMessage = null)
    {
        return new OperationHistoryEntry
        {
            Type = type,
            ProjectName = projectName,
            Description = $"{type} operation on {projectName}",
            Success = success,
            ErrorMessage = errorMessage,
            Duration = TimeSpan.FromSeconds(2),
            ProjectPath = "/src/TestProject/TestProject.csproj",
            PackageId = "Newtonsoft.Json",
            PackageVersion = "13.0.3"
        };
    }

    public static string CreateValidCsproj(
        string targetFramework = "net9.0",
        params (string id, string version)[] packages)
    {
        var packageRefs = string.Join("\n    ",
            packages.Select(p => $"<PackageReference Include=\"{p.id}\" Version=\"{p.version}\" />"));

        return $@"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>{targetFramework}</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    {packageRefs}
  </ItemGroup>
</Project>";
    }

    public static string CreateNuGetConfig(params (string key, string value)[] sources)
    {
        var adds = string.Join("\n    ",
            sources.Select(s => $"<add key=\"{s.key}\" value=\"{s.value}\" />"));

        return $@"<?xml version=""1.0"" encoding=""utf-8""?>
<configuration>
  <packageSources>
    {adds}
  </packageSources>
</configuration>";
    }
}
