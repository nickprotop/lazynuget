using FluentAssertions;
using LazyNuGet.Models;
using LazyNuGet.Tests.Helpers;

namespace LazyNuGet.Tests.Orchestrators;

/// <summary>
/// Tests for the framework compatibility detection logic in SearchCoordinator.
/// HasFrameworkIncompatibility is private/static, so we exercise it via a thin
/// reflection helper to keep tests focused without leaking a public API.
/// </summary>
public class SearchCoordinatorFrameworkTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool HasIncompatibility(ProjectInfo project, NuGetPackage package)
    {
        var method = typeof(LazyNuGet.Orchestrators.SearchCoordinator)
            .GetMethod("HasFrameworkIncompatibility",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        return method != null && (bool)method.Invoke(null, new object[] { project, package })!;
    }

    private static NuGetPackage PackageWith(params string[] frameworks)
    {
        var pkg = SampleDataBuilder.CreateNuGetPackage();
        pkg.TargetFrameworks = frameworks.ToList();
        return pkg;
    }

    private static ProjectInfo ProjectWith(string targetFramework, params string[] extraFrameworks)
    {
        var project = SampleDataBuilder.CreateProjectInfo(targetFramework: targetFramework);
        if (extraFrameworks.Length > 0)
            project.TargetFrameworks = extraFrameworks.ToList();
        return project;
    }

    // ── No frameworks listed ─────────────────────────────────────────────────

    [Fact]
    public void NoPackageFrameworks_ReturnsCompatible()
    {
        var project = ProjectWith("net9.0");
        var package = PackageWith(); // no TF listed

        HasIncompatibility(project, package).Should().BeFalse();
    }

    // ── Legacy-only package vs modern project ─────────────────────────────────

    [Fact]
    public void Net45Package_Net9Project_ReturnsIncompatible()
    {
        var project = ProjectWith("net9.0");
        var package = PackageWith("net45");

        HasIncompatibility(project, package).Should().BeTrue();
    }

    [Fact]
    public void Net48Package_Net9Project_ReturnsIncompatible()
    {
        var project = ProjectWith("net9.0");
        var package = PackageWith("net48");

        HasIncompatibility(project, package).Should().BeTrue();
    }

    [Fact]
    public void Net40Package_Net8Project_ReturnsIncompatible()
    {
        var project = ProjectWith("net8.0");
        var package = PackageWith("net40");

        HasIncompatibility(project, package).Should().BeTrue();
    }

    [Fact]
    public void Net46And47Package_Net9Project_ReturnsIncompatible()
    {
        var project = ProjectWith("net9.0");
        var package = PackageWith("net46", "net47");

        HasIncompatibility(project, package).Should().BeTrue();
    }

    // ── Modern package vs modern project ─────────────────────────────────────

    [Fact]
    public void NetStandardPackage_Net9Project_ReturnsCompatible()
    {
        var project = ProjectWith("net9.0");
        var package = PackageWith("netstandard2.0");

        HasIncompatibility(project, package).Should().BeFalse();
    }

    [Fact]
    public void Net6Package_Net9Project_ReturnsCompatible()
    {
        var project = ProjectWith("net9.0");
        var package = PackageWith("net6.0");

        HasIncompatibility(project, package).Should().BeFalse();
    }

    [Fact]
    public void Net9Package_Net9Project_ReturnsCompatible()
    {
        var project = ProjectWith("net9.0");
        var package = PackageWith("net9.0");

        HasIncompatibility(project, package).Should().BeFalse();
    }

    [Fact]
    public void NetCoreApp31Package_Net9Project_ReturnsCompatible()
    {
        var project = ProjectWith("net9.0");
        var package = PackageWith("netcoreapp3.1");

        HasIncompatibility(project, package).Should().BeFalse();
    }

    // ── Mixed legacy + modern package ────────────────────────────────────────

    [Fact]
    public void MixedNet45AndNetStandard_Net9Project_ReturnsCompatible()
    {
        // Package supports both legacy and modern → not all-legacy → compatible
        var project = ProjectWith("net9.0");
        var package = PackageWith("net45", "netstandard2.0");

        HasIncompatibility(project, package).Should().BeFalse();
    }

    // ── Legacy project with legacy package ───────────────────────────────────

    [Fact]
    public void Net45Package_Net472Project_ReturnsCompatible()
    {
        // Both are legacy — no incompatibility flagged (project is not net5+)
        var project = ProjectWith("net472");
        var package = PackageWith("net45");

        HasIncompatibility(project, package).Should().BeFalse();
    }

    // ── Multi-target project ──────────────────────────────────────────────────

    [Fact]
    public void LegacyPackage_MultiTfProjectWithNet9_ReturnsIncompatible()
    {
        var project = SampleDataBuilder.CreateProjectInfo(targetFramework: "net8.0");
        project.TargetFrameworks = new List<string> { "net8.0", "net9.0" };
        var package = PackageWith("net45");

        HasIncompatibility(project, package).Should().BeTrue();
    }

    [Fact]
    public void LegacyPackage_MultiTfProjectOnlyLegacy_ReturnsCompatible()
    {
        var project = SampleDataBuilder.CreateProjectInfo(targetFramework: "net472");
        project.TargetFrameworks = new List<string> { "net45", "net472" };
        var package = PackageWith("net45");

        HasIncompatibility(project, package).Should().BeFalse();
    }
}
