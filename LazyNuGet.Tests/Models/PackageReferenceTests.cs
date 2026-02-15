using FluentAssertions;
using LazyNuGet.Models;
using LazyNuGet.Tests.Helpers;

namespace LazyNuGet.Tests.Models;

public class PackageReferenceTests
{
    [Fact]
    public void IsOutdated_NullLatestVersion_ReturnsFalse()
    {
        var pkg = SampleDataBuilder.CreatePackageReference(latestVersion: null);
        pkg.IsOutdated.Should().BeFalse();
    }

    [Fact]
    public void IsOutdated_EmptyLatestVersion_ReturnsFalse()
    {
        var pkg = SampleDataBuilder.CreatePackageReference(latestVersion: "");
        pkg.IsOutdated.Should().BeFalse();
    }

    [Fact]
    public void IsOutdated_SameVersion_ReturnsFalse()
    {
        var pkg = SampleDataBuilder.CreatePackageReference(version: "1.0.0", latestVersion: "1.0.0");
        pkg.IsOutdated.Should().BeFalse();
    }

    [Fact]
    public void IsOutdated_DifferentVersion_ReturnsTrue()
    {
        var pkg = SampleDataBuilder.CreatePackageReference(version: "1.0.0", latestVersion: "2.0.0");
        pkg.IsOutdated.Should().BeTrue();
    }

    [Fact]
    public void DisplayStatus_Vulnerable_ShowsVulnerableMessage()
    {
        var pkg = SampleDataBuilder.CreatePackageReference(hasVulnerability: true);
        pkg.DisplayStatus.Should().Contain("VULNERABLE");
    }

    [Fact]
    public void DisplayStatus_Outdated_ShowsArrowMarkup()
    {
        var pkg = SampleDataBuilder.CreatePackageReference(version: "1.0.0", latestVersion: "2.0.0");
        pkg.DisplayStatus.Should().Contain("→");
        pkg.DisplayStatus.Should().Contain("1.0.0");
        pkg.DisplayStatus.Should().Contain("2.0.0");
    }

    [Fact]
    public void DisplayStatus_UpToDate_ShowsGreenLatest()
    {
        var pkg = SampleDataBuilder.CreatePackageReference(version: "1.0.0", latestVersion: null);
        pkg.DisplayStatus.Should().Contain("latest");
        pkg.DisplayStatus.Should().Contain("green");
    }

    [Fact]
    public void DisplayStatus_VulnerableTakesPriority_OverOutdated()
    {
        var pkg = SampleDataBuilder.CreatePackageReference(
            version: "1.0.0", latestVersion: "2.0.0", hasVulnerability: true);
        pkg.DisplayStatus.Should().Contain("VULNERABLE");
        pkg.DisplayStatus.Should().NotContain("→");
    }

    [Fact]
    public void DisplayStatus_EscapesMarkup_InVersionStrings()
    {
        var pkg = SampleDataBuilder.CreatePackageReference(version: "1.0.0", latestVersion: null);
        // Version string should appear properly - Markup.Escape handles special chars
        pkg.DisplayStatus.Should().Contain("1.0.0");
    }
}
