using FluentAssertions;
using LazyNuGet.Models;
using LazyNuGet.Tests.Helpers;

namespace LazyNuGet.Tests.Models;

public class ProjectInfoTests
{
    [Fact]
    public void OutdatedCount_EmptyPackageList_ReturnsZero()
    {
        var project = SampleDataBuilder.CreateProjectInfo();
        project.OutdatedCount.Should().Be(0);
    }

    [Fact]
    public void OutdatedCount_NoOutdated_ReturnsZero()
    {
        var packages = new List<PackageReference>
        {
            SampleDataBuilder.CreatePackageReference(latestVersion: null),
            SampleDataBuilder.CreatePackageReference(id: "Serilog", latestVersion: "3.0.0", version: "3.0.0")
        };
        var project = SampleDataBuilder.CreateProjectInfo(packages: packages);
        project.OutdatedCount.Should().Be(0);
    }

    [Fact]
    public void OutdatedCount_SomeOutdated_ReturnsCorrectCount()
    {
        var packages = new List<PackageReference>
        {
            SampleDataBuilder.CreatePackageReference(version: "1.0.0", latestVersion: "2.0.0"),
            SampleDataBuilder.CreatePackageReference(id: "Serilog", latestVersion: null),
            SampleDataBuilder.CreatePackageReference(id: "xunit", version: "2.0.0", latestVersion: "3.0.0")
        };
        var project = SampleDataBuilder.CreateProjectInfo(packages: packages);
        project.OutdatedCount.Should().Be(2);
    }

    [Fact]
    public void OutdatedCount_AllOutdated_ReturnsTotal()
    {
        var packages = new List<PackageReference>
        {
            SampleDataBuilder.CreatePackageReference(version: "1.0.0", latestVersion: "2.0.0"),
            SampleDataBuilder.CreatePackageReference(id: "Serilog", version: "1.0.0", latestVersion: "3.0.0")
        };
        var project = SampleDataBuilder.CreateProjectInfo(packages: packages);
        project.OutdatedCount.Should().Be(2);
    }

    [Fact]
    public void VulnerableCount_EmptyPackageList_ReturnsZero()
    {
        var project = SampleDataBuilder.CreateProjectInfo();
        project.VulnerableCount.Should().Be(0);
    }

    [Fact]
    public void VulnerableCount_NoVulnerable_ReturnsZero()
    {
        var packages = new List<PackageReference>
        {
            SampleDataBuilder.CreatePackageReference(hasVulnerability: false),
            SampleDataBuilder.CreatePackageReference(id: "Serilog", hasVulnerability: false)
        };
        var project = SampleDataBuilder.CreateProjectInfo(packages: packages);
        project.VulnerableCount.Should().Be(0);
    }

    [Fact]
    public void VulnerableCount_SomeVulnerable_ReturnsCorrectCount()
    {
        var packages = new List<PackageReference>
        {
            SampleDataBuilder.CreatePackageReference(hasVulnerability: true),
            SampleDataBuilder.CreatePackageReference(id: "Serilog", hasVulnerability: false),
            SampleDataBuilder.CreatePackageReference(id: "xunit", hasVulnerability: true)
        };
        var project = SampleDataBuilder.CreateProjectInfo(packages: packages);
        project.VulnerableCount.Should().Be(2);
    }

    [Fact]
    public void DefaultValues_AreSet()
    {
        var project = new ProjectInfo();
        project.Name.Should().BeEmpty();
        project.FilePath.Should().BeEmpty();
        project.TargetFramework.Should().BeEmpty();
        project.Packages.Should().NotBeNull();
        project.Packages.Should().BeEmpty();
    }
}
