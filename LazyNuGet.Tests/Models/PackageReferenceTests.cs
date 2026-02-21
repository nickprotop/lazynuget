using FluentAssertions;
using LazyNuGet.Models;
using LazyNuGet.Tests.Helpers;

namespace LazyNuGet.Tests.Models;

public class PackageReferenceTests
{
    // ── VersionSource / CPM ───────────────────────────────────────────────────

    [Fact]
    public void VersionSource_Default_IsInline()
    {
        var pkg = SampleDataBuilder.CreatePackageReference();
        pkg.VersionSource.Should().Be(VersionSource.Inline);
    }

    [Fact]
    public void VersionSource_Central_CanBeSet()
    {
        var pkg = SampleDataBuilder.CreatePackageReference();
        pkg.VersionSource = VersionSource.Central;
        pkg.VersionSource.Should().Be(VersionSource.Central);
    }

    [Fact]
    public void VersionSource_Override_CanBeSet()
    {
        var pkg = SampleDataBuilder.CreatePackageReference();
        pkg.VersionSource = VersionSource.Override;
        pkg.VersionSource.Should().Be(VersionSource.Override);
    }

    [Fact]
    public void PropsFilePath_Default_IsNull()
    {
        var pkg = SampleDataBuilder.CreatePackageReference();
        pkg.PropsFilePath.Should().BeNull();
    }

    [Fact]
    public void PropsFilePath_CanBeSet()
    {
        var pkg = SampleDataBuilder.CreatePackageReference();
        pkg.PropsFilePath = "/repo/Directory.Packages.props";
        pkg.PropsFilePath.Should().Be("/repo/Directory.Packages.props");
    }

    // ── HasNewerPrerelease ────────────────────────────────────────────────────

    [Fact]
    public void HasNewerPrerelease_NullPrerelease_ReturnsFalse()
    {
        var pkg = SampleDataBuilder.CreatePackageReference(version: "1.0.0", latestVersion: "1.0.0");
        pkg.LatestPrereleaseVersion = null;
        pkg.HasNewerPrerelease.Should().BeFalse();
    }

    [Fact]
    public void HasNewerPrerelease_EmptyPrerelease_ReturnsFalse()
    {
        var pkg = SampleDataBuilder.CreatePackageReference(version: "1.0.0", latestVersion: "1.0.0");
        pkg.LatestPrereleaseVersion = "";
        pkg.HasNewerPrerelease.Should().BeFalse();
    }

    [Fact]
    public void HasNewerPrerelease_WhenAlreadyOutdated_ReturnsFalse()
    {
        // If stable track is already outdated, prerelease hint is suppressed
        var pkg = SampleDataBuilder.CreatePackageReference(version: "1.0.0", latestVersion: "2.0.0");
        pkg.LatestPrereleaseVersion = "2.1.0-beta.1";
        pkg.HasNewerPrerelease.Should().BeFalse();
    }

    [Fact]
    public void HasNewerPrerelease_UpToDate_WithDifferentPrerelease_ReturnsTrue()
    {
        var pkg = SampleDataBuilder.CreatePackageReference(version: "1.0.0", latestVersion: "1.0.0");
        pkg.LatestPrereleaseVersion = "1.1.0-beta.1";
        pkg.HasNewerPrerelease.Should().BeTrue();
    }

    [Fact]
    public void HasNewerPrerelease_PrereleaseMatchesLatest_ReturnsFalse()
    {
        // If LatestPrereleaseVersion == LatestVersion, no separate hint needed
        var pkg = SampleDataBuilder.CreatePackageReference(version: "1.0.0", latestVersion: "1.0.0");
        pkg.LatestPrereleaseVersion = "1.0.0";
        pkg.HasNewerPrerelease.Should().BeFalse();
    }


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
