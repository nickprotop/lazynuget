using FluentAssertions;
using LazyNuGet.Models;
using LazyNuGet.Repositories;
using LazyNuGet.Services;

namespace LazyNuGet.Tests.Models;

public class DataModelTests
{
    [Fact]
    public void NuGetPackage_DefaultValues()
    {
        var pkg = new NuGetPackage();
        pkg.Id.Should().BeEmpty();
        pkg.Version.Should().BeEmpty();
        pkg.Description.Should().BeEmpty();
        pkg.TotalDownloads.Should().Be(0);
        pkg.Versions.Should().NotBeNull().And.BeEmpty();
        pkg.Authors.Should().NotBeNull().And.BeEmpty();
        pkg.Tags.Should().NotBeNull().And.BeEmpty();
        pkg.Dependencies.Should().NotBeNull().And.BeEmpty();
        pkg.IsVerified.Should().BeFalse();
        pkg.IsDeprecated.Should().BeFalse();
    }

    [Fact]
    public void PackageReference_DefaultValues()
    {
        var pkg = new PackageReference();
        pkg.Id.Should().BeEmpty();
        pkg.Version.Should().BeEmpty();
        pkg.LatestVersion.Should().BeNull();
        pkg.HasVulnerability.Should().BeFalse();
        pkg.LastUpdated.Should().BeNull();
    }

    [Fact]
    public void ProjectInfo_DefaultValues()
    {
        var project = new ProjectInfo();
        project.Packages.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void DependencyNode_DefaultValues()
    {
        var node = new DependencyNode();
        node.PackageId.Should().BeEmpty();
        node.ResolvedVersion.Should().BeEmpty();
        node.RequestedVersion.Should().BeNull();
        node.IsTransitive.Should().BeFalse();
    }

    [Fact]
    public void ProjectDependencyTree_DefaultValues()
    {
        var tree = new ProjectDependencyTree();
        tree.ProjectName.Should().BeEmpty();
        tree.TargetFramework.Should().BeEmpty();
        tree.TopLevelPackages.Should().NotBeNull().And.BeEmpty();
        tree.TransitivePackages.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void OperationResult_DefaultValues()
    {
        var result = new OperationResult();
        result.Success.Should().BeFalse();
        result.Message.Should().BeEmpty();
        result.ErrorDetails.Should().BeNull();
        result.ExitCode.Should().Be(0);
    }

    [Fact]
    public void OperationHistoryEntry_DefaultValues()
    {
        var entry = new OperationHistoryEntry();
        entry.Id.Should().NotBe(Guid.Empty);
        entry.ProjectName.Should().BeEmpty();
        entry.Description.Should().BeEmpty();
        entry.Success.Should().BeFalse();
    }

    [Fact]
    public void NuGetSource_DefaultValues()
    {
        var source = new NuGetSource();
        source.Name.Should().BeEmpty();
        source.Url.Should().BeEmpty();
        source.IsEnabled.Should().BeTrue();
        source.RequiresAuth.Should().BeFalse();
    }

    [Fact]
    public void PackageDependencyGroup_DefaultValues()
    {
        var group = new PackageDependencyGroup();
        group.TargetFramework.Should().BeEmpty();
        group.Packages.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void ProjectFileData_DefaultValues()
    {
        var data = new ProjectFileData();
        data.FilePath.Should().BeEmpty();
        data.Name.Should().BeEmpty();
        data.TargetFramework.Should().BeEmpty();
        data.PackageReferences.Should().NotBeNull().And.BeEmpty();
    }
}
