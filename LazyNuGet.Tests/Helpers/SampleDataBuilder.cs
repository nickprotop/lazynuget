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

    /// <summary>
    /// Create a Directory.Packages.props file with the given package version entries.
    /// </summary>
    public static string CreatePropsFile(params (string id, string version)[] packages)
    {
        var entries = string.Join("\n    ",
            packages.Select(p => $"<PackageVersion Include=\"{p.id}\" Version=\"{p.version}\" />"));

        return $@"<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    {entries}
  </ItemGroup>
</Project>";
    }

    /// <summary>
    /// Create a CPM-enabled .csproj that references packages without inline versions.
    /// </summary>
    public static string CreateCpmCsproj(
        string targetFramework = "net9.0",
        params string[] packageIds)
    {
        var refs = string.Join("\n    ",
            packageIds.Select(id => $"<PackageReference Include=\"{id}\" />"));

        return $@"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>{targetFramework}</TargetFramework>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    {refs}
  </ItemGroup>
</Project>";
    }

    /// <summary>
    /// Create a packages.config XML file with the given packages.
    /// </summary>
    public static string CreatePackagesConfig(
        params (string id, string version, string targetFramework)[] packages)
    {
        var entries = packages.Length == 0
            ? string.Empty
            : string.Join("\n  ", packages.Select(p =>
                $"<package id=\"{p.id}\" version=\"{p.version}\" targetFramework=\"{p.targetFramework}\" />"));

        return $@"<?xml version=""1.0"" encoding=""utf-8""?>
<packages>
  {entries}
</packages>";
    }

    /// <summary>
    /// Create an old-style .csproj that uses packages.config (legacy .NET Framework format).
    /// Includes:
    ///  - NuGet hint-path <Reference> elements for each package (will be migrated)
    ///  - Framework <Reference> elements without HintPath (must be preserved after migration)
    ///  - A NuGet MSBuild import (will be removed on migration)
    /// </summary>
    public static string CreateLegacyCsproj(
        params (string id, string version)[] nugetPackages)
    {
        var nugetRefs = string.Join("\n    ", nugetPackages.Select(p =>
            $"<Reference Include=\"{p.id}\">\n" +
            $"      <HintPath>..\\packages\\{p.id}.{p.version}\\lib\\net45\\{p.id}.dll</HintPath>\n" +
            $"    </Reference>"));

        var frameworkRefs = @"<Reference Include=""System"" />
    <Reference Include=""System.Core"" />
    <Reference Include=""System.Xml"" />";

        return $@"<?xml version=""1.0"" encoding=""utf-8""?>
<Project ToolsVersion=""15.0"" xmlns=""http://schemas.microsoft.com/developer/msbuild/2003"">
  <Import Project=""$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.CSharp.targets"" />
  <Import Project=""..\packages\NuGet.Build.Tasks.Pack.0.0.1\build\NuGet.Build.Tasks.Pack.targets"" Condition=""Exists('..\packages\NuGet.Build.Tasks.Pack.0.0.1\build\NuGet.Build.Tasks.Pack.targets')"" />
  <PropertyGroup>
    <TargetFrameworkVersion>v4.7.2</TargetFrameworkVersion>
    <AssemblyName>LegacyProject</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    {frameworkRefs}
    {nugetRefs}
  </ItemGroup>
</Project>";
    }

    /// <summary>
    /// Create a minimal .sln file referencing the given projects.
    /// </summary>
    public static string CreateSolutionFile(params (string name, string relativePath)[] projects)
    {
        var projectEntries = string.Join("\n",
            projects.Select(p =>
                $"Project(\"{{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}}\") = \"{p.name}\", \"{p.relativePath}\", \"{{{Guid.NewGuid()}}}\"\nEndProject"));

        return $@"
Microsoft Visual Studio Solution File, Format Version 12.00
# Visual Studio Version 17
{projectEntries}
Global
EndGlobal
";
    }
}
